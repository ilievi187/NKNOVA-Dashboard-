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
        echo '[]';;
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
