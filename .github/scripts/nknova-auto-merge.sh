#!/usr/bin/env bash
set -euo pipefail

repo="${GH_REPO:-ilievi187/NKNOVA-Dashboard-}"
owner="${repo%%/*}"
dry_run="${NKNOVA_AUTO_MERGE_DRY_RUN:-true}"

if [[ "$repo" != "ilievi187/NKNOVA-Dashboard-" || "$owner" != "ilievi187" ]]; then
    echo "Unexpected repository context; refusing to merge."
    exit 1
fi

if [[ "${TRIGGER_EVENT:-manual}" == "workflow_run" ]]; then
    if [[ "${UPSTREAM_EVENT:-}" != "pull_request" || "${UPSTREAM_CONCLUSION:-}" != "success" ]]; then
        echo "Ignoring non-successful or non-PR workflow_run event."
        exit 0
    fi
fi

required_checks=(
    "Package tests (ubuntu-latest)"
    "Package tests (windows-latest)"
    "Linux solution build and package"
    "Linux solution test, build and package"
    "ExecutableResolver tests (ubuntu-latest)"
    "ExecutableResolver tests (windows-latest)"
    "Services redaction regression (ubuntu-latest)"
    "Services redaction regression (windows-latest)"
    "Detect Rust sources"
    "CodeQL (actions)"
    "CodeQL (csharp)"
    "CodeQL" # GitHub code-scanning aggregate: analysis success alone does not clear findings.
    "CodeQL (rust)"
    "Dependency Manifest Gate"
)

ensure_main_healthy() {
    local main_sha runs latest status conclusion
    main_sha=$(gh api "repos/${repo}/commits/main" --jq '.sha')
    runs=$(gh api "repos/${repo}/actions/workflows/nknova-ci.yml/runs?branch=main&per_page=100")
    latest=$(jq -c --arg sha "$main_sha" '[.workflow_runs[] | select(.head_sha == $sha)] | sort_by(.created_at) | last // {}' <<<"$runs")
    status=$(jq -r '.status // "missing"' <<<"$latest")
    conclusion=$(jq -r '.conclusion // "missing"' <<<"$latest")
    if [[ "$status" != "completed" || "$conclusion" != "success" ]]; then
        echo "Main is not healthy at ${main_sha}: NKNOVA CI status=${status}, conclusion=${conclusion}. Stopping auto-merge."
        return 1
    fi
    echo "Main health confirmed at ${main_sha}."
}

wait_for_main_workflow() {
    local workflow_file="$1"
    local expected_sha="$2"
    local attempt runs latest run_id status conclusion

    for attempt in $(seq 1 180); do
        runs=$(gh api "repos/${repo}/actions/workflows/${workflow_file}/runs?branch=main&event=workflow_dispatch&per_page=100")
        latest=$(jq -c --arg sha "$expected_sha" '[.workflow_runs[] | select(.head_sha == $sha)] | sort_by(.created_at) | last // {}' <<<"$runs")
        run_id=$(jq -r '.id // empty' <<<"$latest")
        status=$(jq -r '.status // "missing"' <<<"$latest")
        conclusion=$(jq -r '.conclusion // ""' <<<"$latest")

        if [[ -n "$run_id" && "$status" == "completed" ]]; then
            if [[ "$conclusion" == "success" ]]; then
                echo "Post-merge ${workflow_file} passed for ${expected_sha} (run ${run_id})."
                return 0
            fi
            echo "Post-merge ${workflow_file} failed for ${expected_sha} (run ${run_id}, conclusion=${conclusion})."
            return 1
        fi

        if (( attempt % 6 == 0 )); then
            echo "Waiting for ${workflow_file} on ${expected_sha}: status=${status}, attempt=${attempt}/180."
        fi
        sleep 10
    done

    echo "Timed out waiting for ${workflow_file} on ${expected_sha}."
    return 1
}

# Fail closed if the repository's code-scanning alert inventory cannot be read.
# Aggregate CodeQL success only proves the PR introduced no newly-blocking alert;
# it does not certify that pre-existing alerts are resolved.
ensure_no_open_code_scanning_alerts() {
    local pages open_count
    if ! pages=$(gh api --paginate --slurp "repos/${repo}/code-scanning/alerts?state=open&per_page=100"); then
        echo "Cannot read code-scanning alerts; refusing automatic merge."
        return 1
    fi

    if ! jq -e 'type == "array" and all(.[]; type == "array" and all(.[]; type == "object" and .state == "open"))' <<<"$pages" >/dev/null; then
        echo "Unexpected code-scanning response or non-open alert state; refusing automatic merge."
        return 1
    fi

    open_count=$(jq '[.[][] | select(.state == "open")] | length' <<<"$pages")
    if (( open_count > 0 )); then
        echo "Found ${open_count} unresolved code-scanning alerts; manual security review and merge required."
        return 1
    fi
}

ensure_main_healthy
ensure_no_open_code_scanning_alerts

prs_json=$(gh pr list --repo "$repo" --state open --base main --limit 100 \
    --json number,createdAt,author,isDraft,baseRefName,headRepository,headRepositoryOwner)
mapfile -t prs < <(
    jq -r --arg owner "$owner" --arg repo "$repo" '
        sort_by(.createdAt)[]
        | select(.author.login == $owner)
        | select(.isDraft == false and .baseRefName == "main")
        | select(.headRepository.nameWithOwner == $repo)
        | select(.headRepositoryOwner.login == $owner)
        | .number
    ' <<<"$prs_json"
)

if (( ${#prs[@]} == 0 )); then
    echo "No open, non-draft, same-repository owner PRs target main."
    exit 0
fi

for number in "${prs[@]}"; do
    pr=$(gh pr view "$number" --repo "$repo" \
        --json number,state,isDraft,author,body,baseRefName,headRefOid,headRepository,headRepositoryOwner,mergeable,mergeStateStatus,files)
    state=$(jq -r '.state' <<<"$pr")
    draft=$(jq -r '.isDraft' <<<"$pr")
    author=$(jq -r '.author.login // ""' <<<"$pr")
    base=$(jq -r '.baseRefName // ""' <<<"$pr")
    head_repo=$(jq -r '.headRepository.nameWithOwner // ""' <<<"$pr")
    head_owner=$(jq -r '.headRepositoryOwner.login // ""' <<<"$pr")
    head_sha=$(jq -r '.headRefOid // ""' <<<"$pr")

    if [[ "$state" != "OPEN" || "$draft" != "false" || "$author" != "$owner" || "$base" != "main" || "$head_repo" != "$repo" || "$head_owner" != "$owner" ]]; then
        echo "Skipping PR #${number}: it is not an open, ready, same-repository owner PR targeting main."
        continue
    fi

    body=$(jq -r '.body // ""' <<<"$pr")
    if ! grep -Fq -- '[x] I reviewed source provenance and license attribution for this change.' <<<"$body"; then
        echo "Skipping PR #${number}: the source/license provenance attestation is not checked."
        continue
    fi

    paths=$(jq -r '.files[].path' <<<"$pr")
    if grep -Eq '(^|/)LICENSE([.][^/]+)?$|(^|/)COPYING([.][^/]+)?$|(^|/)NOTICE([.][^/]+)?$|^\.github/(workflows|scripts)/|^\.github/dependency-review-config[.]yml$' <<<"$paths"; then
        echo "Skipping PR #${number}: workflow, automation, license, or notice changes require manual review."
        continue
    fi

    dependency_manifest=false
    while IFS= read -r path; do
        [[ -n "$path" ]] || continue
        case "$path" in
            Nolvus.Services.Tests/Nolvus.Services.Tests.csproj)
                patch=$(gh pr diff "$number" --repo "$repo")
                if grep -Eq '^[+][^+].*<(PackageReference|PackageVersion|PackageDownload)([[:space:]>])' <<<"$patch"; then
                    dependency_manifest=true
                fi
                ;;
            *.csproj|*.props|*.targets|*/packages.lock.json|packages.lock.json|*/packages.config|packages.config|*/Cargo.toml|Cargo.toml|*/Cargo.lock|Cargo.lock|NuGet.config|nuget.config|global.json)
                dependency_manifest=true
                ;;
        esac
    done <<<"$paths"
    if [[ "$dependency_manifest" == true ]]; then
        echo "Skipping PR #${number}: dependency-manifest changes need explicit license/advisory review."
        continue
    fi

    merge_state=$(jq -r '.mergeStateStatus // "UNKNOWN"' <<<"$pr")
    mergeable=$(jq -r '.mergeable // "UNKNOWN"' <<<"$pr")
    if [[ "$merge_state" == "BEHIND" ]]; then
        echo "Skipping PR #${number}: its branch is behind main and needs a fresh merge update plus rerun checks."
        continue
    fi
    if [[ "$merge_state" != "CLEAN" || "$mergeable" != "MERGEABLE" ]]; then
        echo "Skipping PR #${number}: mergeability is ${merge_state}/${mergeable}."
        continue
    fi

    checks=$(gh api "repos/${repo}/commits/${head_sha}/check-runs?per_page=100")
    ready=true
    for context in "${required_checks[@]}"; do
        expected_app="github-actions"
        if [[ "$context" == "CodeQL" ]]; then
            expected_app="github-advanced-security"
        fi
        check_state=$(jq -r --arg name "$context" --arg expected_app "$expected_app" '
            ([.check_runs[] | select(.name == $name and .app.slug == $expected_app)] | sort_by(.started_at // .created_at // "") | last // {}) as $check
            | if $check.status == "completed" then ($check.conclusion // "") else ($check.status // "missing") end
        ' <<<"$checks")
        if [[ "$context" == "CodeQL (rust)" && "$check_state" == "skipped" ]]; then
            echo "CodeQL Rust check is skipped because the successful detection job found no Rust sources."
            continue
        fi
        if [[ "$check_state" != "success" ]]; then
            echo "PR #${number} is waiting for required check '${context}' (state=${check_state})."
            ready=false
            break
        fi
    done
    if [[ "$ready" != true ]]; then
        continue
    fi

    current_sha=$(gh pr view "$number" --repo "$repo" --json headRefOid --jq '.headRefOid')
    if [[ "$current_sha" != "$head_sha" ]]; then
        echo "PR #${number} changed while checks were evaluated; refusing stale-head merge."
        continue
    fi

    if [[ "$dry_run" == "true" ]]; then
        echo "DRY RUN: PR #${number} is eligible for a squash merge at ${head_sha}; no merge requested."
        continue
    fi

    echo "All required checks passed for trusted owner PR #${number} at ${head_sha}. Requesting a normal squash merge with no admin bypass."
    if ! gh pr merge "$number" --repo "$repo" --squash; then
        echo "GitHub refused PR #${number}; branch protection or a newer conflict remains. Leaving it open."
        continue
    fi

    merged=$(gh pr view "$number" --repo "$repo" --json state,mergeCommit)
    if [[ $(jq -r '.state' <<<"$merged") != "MERGED" ]]; then
        echo "PR #${number} did not reach MERGED state after the merge request. Halting."
        exit 1
    fi
    merge_sha=$(jq -r '.mergeCommit.oid' <<<"$merged")
    echo "PR #${number} merged at ${merge_sha}. Dispatching post-merge acceptance workflows."

    for workflow_file in nknova-ci.yml nolvus-core-tests.yml nknova-security-gates.yml; do
        gh api --method POST "repos/${repo}/actions/workflows/${workflow_file}/dispatches" -f ref=main
    done

    wait_for_main_workflow nknova-ci.yml "$merge_sha"
    wait_for_main_workflow nolvus-core-tests.yml "$merge_sha"
    wait_for_main_workflow nknova-security-gates.yml "$merge_sha"

    ensure_main_healthy

done
