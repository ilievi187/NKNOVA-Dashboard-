#!/usr/bin/env bash
set -euo pipefail

# Hermetic regression tests: no GitHub API calls and no merges.
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
mkdir -p "$tmp/bin"
cat > "$tmp/bin/gh" <<'MOCK'
#!/usr/bin/env bash
set -euo pipefail
case "$*" in
    "api repos/ilievi187/NKNOVA-Dashboard-/commits/main --jq .sha")
        echo 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa';;
    "api repos/ilievi187/NKNOVA-Dashboard-/actions/workflows/nknova-ci.yml/runs?branch=main&per_page=100")
        echo '{"workflow_runs":[{"head_sha":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","created_at":"2026-01-01T00:00:00Z","status":"completed","conclusion":"success"}]}';;
    "api --paginate --slurp repos/ilievi187/NKNOVA-Dashboard-/code-scanning/alerts?state=open&per_page=100")
        case "${NKNOVA_ALERT_FIXTURE:-}" in
            clean) echo '[[]]';;
            appeared)
                # Simulate a new finding between the initial inventory and the merge decision.
                count=0
                if [[ -f "${NKNOVA_ALERT_STATE_FILE:-/dev/null}" ]]; then
                    read -r count < "$NKNOVA_ALERT_STATE_FILE"
                fi
                printf '%s\n' "$((count + 1))" > "$NKNOVA_ALERT_STATE_FILE"
                if (( count == 0 )); then
                    echo '[[]]'
                else
                    echo '[[{"state":"open","number":43}]]'
                fi;;
            empty_slurp) echo '[]';;
            null_page) echo '[null]';;
            open) echo '[[{"state":"open","number":42}]]';;
            malformed) echo '{"unexpected":"response"}';;
            missing_state) echo '[[{"number":42}]]';;
            wrong_state) echo '[[{"state":"dismissed","number":42}]]';;
            invalid_entry) echo '[["not-an-alert-object"]]';;
            error) exit 22;;
            *) exit 99;;
        esac;;
    "pr list "*)
        if [[ -n "${NKNOVA_CHECK_FIXTURE:-}" ]]; then
            echo '[{"number":7,"createdAt":"2026-10-26T00:00:00Z","author":{"login":"ilievi187"},"isDraft":false,"baseRefName":"main","headRepository":{"nameWithOwner":"ilievi187/NKNOVA-Dashboard-"},"headRepositoryOwner":{"login":"ilievi187"}}]'
        else
            echo '[]'
        fi;;
    "pr view 7 --repo ilievi187/NKNOVA-Dashboard- --json headRefOid --jq .headRefOid")
        echo 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb';;
    "pr view 7 --repo ilievi187/NKNOVA-Dashboard- --json "*)
        echo '{"number":7,"state":"OPEN","isDraft":false,"author":{"login":"ilievi187"},"body":"[x] I reviewed source provenance and license attribution for this change.","baseRefName":"main","headRefOid":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","headRepository":{"nameWithOwner":"ilievi187/NKNOVA-Dashboard-"},"headRepositoryOwner":{"login":"ilievi187"},"mergeable":"MERGEABLE","mergeStateStatus":"CLEAN","files":[]}';;
    "api repos/ilievi187/NKNOVA-Dashboard-/commits/bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb/check-runs?per_page=100")
        jq -n --arg fixture "$NKNOVA_CHECK_FIXTURE" '
          ["Package tests (ubuntu-latest)", "Package tests (windows-latest)",
           "Linux solution build and package", "Linux solution test, build and package",
           "ExecutableResolver tests (ubuntu-latest)", "ExecutableResolver tests (windows-latest)",
           "Services redaction regression (ubuntu-latest)", "Services redaction regression (windows-latest)",
           "Detect Rust sources", "CodeQL (actions)", "CodeQL (csharp)", "CodeQL",
           "CodeQL (rust)", "Dependency Manifest Gate"]
          | map({name: ., status: "completed", conclusion: "success",
                 app: {slug: (if . == "CodeQL" then "github-advanced-security" else "github-actions" end)}})
          | if $fixture == "missing" then map(select(.name != "CodeQL"))
            elif $fixture == "spoof" then map(if .name == "CodeQL" then .app.slug = "github-actions" else . end)
            elif $fixture == "workflow_spoof" then map(if .name == "CodeQL (csharp)" then .app.slug = "untrusted-app" else . end)
            elif $fixture == "failed" then map(if .name == "CodeQL" then .conclusion = "failure" else . end)
            elif $fixture == "pending" then map(if .name == "CodeQL" then .status = "in_progress" else . end)
            elif $fixture == "rust_skip" then map(if .name == "CodeQL (rust)" then .conclusion = "skipped" else . end)
            elif $fixture == "spoof_after_failure" then
              (map(if .name == "CodeQL" then .conclusion = "failure" else . end)
               + [{name: "CodeQL", status: "completed", conclusion: "success", app: {slug: "github-actions"}}])
            else . end | {check_runs: .}';;
    "pr merge 7 --repo ilievi187/NKNOVA-Dashboard- --squash --match-head-commit bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb")
        # A changed PR head must make GitHub reject the SHA-locked merge.
        printf "sha-guard-used\n" > "${NKNOVA_MERGE_MATCH_MARKER:?}"
        echo "Simulated GitHub rejection: PR head changed" >&2
        exit 1;;
    *)
        echo "Unexpected mocked gh invocation: $*" >&2
        exit 98;;
esac
MOCK
chmod +x "$tmp/bin/gh"

run_case() {
    local fixture="$1" expected="$2" status=0
    PATH="$tmp/bin:$PATH" GH_REPO="ilievi187/NKNOVA-Dashboard-" \
        NKNOVA_AUTO_MERGE_DRY_RUN=true NKNOVA_ALERT_FIXTURE="$fixture" \
        bash "$root/.github/scripts/nknova-auto-merge.sh" >"$tmp/$fixture.log" 2>&1 || status=$?
    if [[ "$expected" == "pass" && "$status" -ne 0 ]] || \
       [[ "$expected" == "fail" && "$status" -eq 0 ]]; then
        echo "Unexpected exit status $status for fixture $fixture:" >&2
        cat "$tmp/$fixture.log" >&2
        exit 1
    fi
    echo "PASS: $fixture ($expected)"
}

bash -n "$root/.github/scripts/nknova-auto-merge.sh"
run_case clean pass
run_case empty_slurp fail
run_case null_page fail
run_case open fail
run_case malformed fail
run_case missing_state fail
run_case wrong_state fail
run_case invalid_entry fail
run_case error fail

# These cases exercise the real PR check gate with mocked check-run provenance.
# All runs are dry-run; a blocked PR is expected to exit 0 after being skipped.
run_check_case() {
    local fixture="$1" expected="$2" status=0 alert_fixture=clean
    if [[ "$fixture" == "late_alert" ]]; then
        alert_fixture=appeared
    fi
    rm -f "$tmp/alert-counter-$fixture"
    PATH="$tmp/bin:$PATH" GH_REPO="ilievi187/NKNOVA-Dashboard-" \
        NKNOVA_AUTO_MERGE_DRY_RUN=true NKNOVA_ALERT_FIXTURE="$alert_fixture" \
        NKNOVA_ALERT_STATE_FILE="$tmp/alert-counter-$fixture" \
        NKNOVA_CHECK_FIXTURE="$fixture" \
        bash "$root/.github/scripts/nknova-auto-merge.sh" >"$tmp/check-$fixture.log" 2>&1 || status=$?
    if [[ "$status" -ne 0 ]] || ! grep -Fq -- "$expected" "$tmp/check-$fixture.log"; then
        echo "Unexpected result for check-run fixture $fixture (exit $status):" >&2
        cat "$tmp/check-$fixture.log" >&2
        exit 1
    fi
    echo "PASS: check-run $fixture"
}

run_check_case success "DRY RUN: PR #7 is eligible"
run_check_case late_alert "Skipping PR #7: code-scanning alerts appeared or could not be rechecked."
run_check_case rust_skip "waiting for required check 'CodeQL (rust)'"
run_check_case missing "waiting for required check 'CodeQL'"
run_check_case spoof "waiting for required check 'CodeQL'"
run_check_case workflow_spoof "waiting for required check 'CodeQL (csharp)'"
run_check_case failed "waiting for required check 'CodeQL'"
run_check_case pending "waiting for required check 'CodeQL'"
run_check_case spoof_after_failure "waiting for required check 'CodeQL'"

# Exercise the non-dry-run merge command against a refusing mock. The exact
# --match-head-commit SHA is required to reach this mock case; no live merge occurs.
run_sha_locked_merge_case() {
    local status=0 marker="$tmp/merge-match-head-marker"
    rm -f "$marker"
    PATH="$tmp/bin:$PATH" GH_REPO="ilievi187/NKNOVA-Dashboard-" \\
        NKNOVA_AUTO_MERGE_DRY_RUN=false NKNOVA_ALERT_FIXTURE=clean \\
        NKNOVA_CHECK_FIXTURE=success NKNOVA_MERGE_MATCH_MARKER="$marker" \\
        bash "$root/.github/scripts/nknova-auto-merge.sh" >"$tmp/merge-sha-guard.log" 2>&1 || status=$?
    if [[ "$status" -ne 0 || ! -f "$marker" ]] || \\
       ! grep -Fq 'GitHub refused PR #7' "$tmp/merge-sha-guard.log"; then
        echo "FAILED: merge was not SHA-locked or stale-head refusal was not handled:" >&2
        cat "$tmp/merge-sha-guard.log" >&2
        exit 1
    fi
    echo "PASS: SHA-locked merge request safely rejects a changed PR head"
}
run_sha_locked_merge_case
