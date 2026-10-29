# PR #29 — CodeQL remediation (owner decision required)

Status: **BLOCKED on owner sign-off.** Do not merge, dismiss, force-push, or self-approve without it.

## The red gate

- PR #29 `fix/nknova-user-scoped-credentials` — head `e180dc5`, `OPEN`, `MERGEABLE`, **draft**, `mergeStateStatus:BLOCKED` (draft only; `main` has **no** required status checks).
- CI: 20/21 checks green. The one red is the **aggregate `CodeQL` / code-scanning gate**, driven by 4 `high` alerts.

## The 4 open alerts — all PRE-EXISTING on `main` (not introduced by PR #29)

| # | rule | severity | location | created | on commit / ref |
|---|------|----------|----------|---------|-----------------|
| 1 | `cs/ecb-encryption` | high | `Nolvus.Services/Lib/LibService.cs:178` | 2026-10-23 | `a8bf1d02` / `main` |
| 2 | `cs/ecb-encryption` | high | `Nolvus.Services/Lib/LibService.cs:203` | 2026-10-23 | `a8bf1d02` / `main` |
| 3 | `cs/weak-encryption` | high | `Nolvus.Services/Lib/LibService.cs:174` | 2026-10-23 | `a8bf1d02` / `main` |
| 4 | `cs/weak-encryption` | high | `Nolvus.Services/Lib/LibService.cs:199` | 2026-10-23 | `a8bf1d02` / `main` |

All four were created **2026-10-23 against `a8bf1d02` on `main`** — before the PR #29 sync. They come from the legacy `TripleDES`/`ECB` + hardcoded-key block. PR #29 *reduces* the legacy surface: on the PR head, `EncryptString` → `UserCredentialProtector.Protect` (AES-GCM `v3`); only the private read-only fallback `DecryptLegacyString` retains legacy crypto.

## Why this is load-bearing (do not delete blindly)

- `DecryptString` legacy fallback is required for **real users' existing credentials**: `StartFrame` (Nolvus password), `GlobalSettingsFrame` (Mega/Nexus), `SettingsService` INI.
- `Nolvus.Services.Tests` (console, `dotnet run`, **not** xUnit) asserts legacy read **and non-mutation**:
  - `Program.cs:106` (PR head `e180dc5`): `FAILED: reading legacy settings rewrote the INI or created credential storage.`
  - `Program.cs:100`: `FAILED: production settings services could not read legacy dummy credentials.`
  - fixture `AI0pbfv2…` / `NKNOVA legacy migration test only`.

## Option A — read-time migration legacy → v3, then remove legacy crypto (preferred, permanent)

**What to change**
1. Add a one-time migration entry point (e.g. `LibService.MigrateLegacyCredentials(IniData)` or a `SettingsService` hook) that, for each credential value lacking the `nknova:v3:` prefix: decrypt legacy → re-encrypt via `EncryptString` (v3) → write back atomically with owner-only perms.
2. Delete `DecryptLegacyString` and the `TripleDES`/`ECB`/hardcoded-key block from `LibService.cs`.
3. Update the 2 test fixtures in `Nolvus.Services.Tests/Program.cs` so they assert **migration** (legacy present before, v3 envelope after) instead of non-mutation.

**Blocking contract conflict (must be resolved first)**
- The PR's **own** test at `Program.cs:106` requires reading to be **non-mutating**. Option A with read-time write-back **violates that test**. So Option A requires either:
  - (a) a **separate explicit migration step** (not a silent read), e.g. an explicit "upgrade credentials" action + one-time boot migration, **and** flipping the `:106` assertion to allow the migration path; or
  - (b) keep read non-mutating and migrate **only** at first-run / settings-save time.
- Either way this is a **breaking behavior change** and needs a **real-user Windows/Linux Nolvus/Mega/Nexus login smoke** after migration — which I **cannot self-verify** (no local .NET SDK on this machine). CI alone is representative, not acceptance.

**Owner action:** approve the migration approach (a or b) + confirm who runs the real-user login smoke. I then implement on `pr29-sync` for review.

## Option B — formal dismiss of the 4 pre-existing alerts (fastest, owner-only)

These are pre-existing on `main`, so dismissing them is an **owner security-gate decision**, not something I self-execute.

- Dismissal reason (copy-paste): `Pre-existing legacy read-only compatibility (TripleDES/ECB). Real-user credential migration tracked as follow-up; new credentials already use AES-GCM v3. Dismissed with owner approval, migration pending.`
- Apply (owner runs, one call per alert 1–4):
  - `gh api -X POST repos/ilievi187/NKNOVA-Dashboard-/code-scanning/alerts/<N>/dismissals -f state=dismissed -F reason="<reason>"` for `N` in `1 2 3 4`.
  - Or: Security → Code scanning → each alert → Dismiss (requires owner permission).

After dismissal: de-draft PR #29 → re-run `CodeQL` → green → merge candidate (still gated on manual security review + real-user evidence per release policy).

## Decision

- **[ ] Option A** — implement migration (approach a or b) on `pr29-sync`, owner reviews + real-user smoke.
- **[ ] Option B** — owner dismisses 4 alerts now; migration tracked as follow-up issue.

## Release blockers (independent of the above)

- Real-user **install + login** evidence: Nolvus password, Mega, Nexus on **Windows and Linux**.
- GUI / end-to-end install + mod download + Mod Organizer + backup/rollback evidence.
- **SixLabors.ImageSharp 3.2.0** — security maintenance window ends **2027-05-12** (pinned in 6 csproj files). Plan an upgrade or documented acceptance before that date.
- Do **not** claim the release stable until the above are evidenced.
