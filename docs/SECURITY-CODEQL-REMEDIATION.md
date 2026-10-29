# PR #29 — CodeQL remediation (owner decision required)

Status: **BLOCKED on owner sign-off.** Do not merge, dismiss, force-push, or self-approve without it.

## The red gate

- PR #29 `fix/nknova-user-scoped-credentials` — head `e180dc5`, `OPEN`, `MERGEABLE`, **draft**, `mergeStateStatus:BLOCKED` (draft only; `main` has **no** required status checks).
- CI: 16 SUCCESS / 1 SKIPPED / **1 FAILURE**. The single red is the **aggregate `CodeQL` check** (from `github-advanced-security`, no workflow). The three language-specific CodeQL sub-checks — `CodeQL (csharp)`, `CodeQL (rust)`, `CodeQL (actions)` — all **SUCCESS**.

### Why the aggregate fails while the language sub-checks pass

The aggregate `CodeQL` gate is a **repository-level code-scanning gate**: it goes red if *any* open code-scanning alert is present in the repository, regardless of which commit introduced it. The four open alerts (below) were created 2026-10-23 against `main`. PR #29's sync moved the legacy `ECB` block from lines 178/203 to line 223 (refactor removed the encrypt-path ECB, kept only the read-only decrypt fallback), so the same rules now annotate a different line — the aggregate gate re-fires on the PR head. The language-specific `CodeQL (csharp)` job only reports *new* findings introduced by the diff on that head; because the legacy path was already flagged on `main`, the csharp job sees no *new* finding and passes. The aggregate gate, by design, does not distinguish pre-existing vs. new.

## The 4 open alerts — all PRE-EXISTING on `main` (not introduced by PR #29)

Severity per GitHub REST API: **`warning`** (not `high`; `blocking: null`, `dismissal_state: null`).

| # | rule | severity | location (on `main`) | location (PR head) | created |
|---|------|----------|----------------------|--------------------|---------|
| 1 | `cs/ecb-encryption` | warning | `LibService.cs:178` | `LibService.cs:223` | 2026-10-23 |
| 2 | `cs/ecb-encryption` | warning | `LibService.cs:203` | `LibService.cs:223` | 2026-10-23 |
| 3 | `cs/weak-encryption` | warning | `LibService.cs:174` | (removed on PR head) | 2026-10-23 |
| 4 | `cs/weak-encryption` | warning | `LibService.cs:199` | (removed on PR head) | 2026-10-23 |

All four were created **2026-10-23 against `a8bf1d02` on `main`** — before the PR #29 sync. On `main`, the legacy `TripleDES`/`ECB` + hardcoded-key block appears in **two** places (`EncryptString` at line 178, `DecryptString` at line 203). PR #29 *reduces* the legacy surface: `EncryptString` now delegates to `UserCredentialProtector.Protect` (AES-GCM `v3`); only the private read-only fallback `DecryptLegacyString` (line 196–235, ECB at line 223) retains legacy crypto.

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
- Either way this is a **breaking behavior change** and needs a **real-user Windows/Linux Nolvus/Mega/Nexus login smoke** after migration. Local build + test **is** available on this machine (`.NET 9.0.319` + `10.0.402` SDKs, confirmed 2026-10-29), so the build/test regression can be self-verified before hand-off; the remaining acceptance gap is **real-user install + login on Windows and Linux**, which local CI cannot substitute.

**Owner action:** approve the migration approach (a or b) + confirm who runs the real-user login smoke. I then implement on `pr29-sync` for review.

## Local validation (2026-10-29, on PR head `e180dc5`)

`.NET 10.0.402` SDK + `9.0.319` present on this Linux host. Ran locally (Release, `net10.0`):

| Check | Result |
|-------|--------|
| `dotnet build` full solution | **0 errors**, 211 pre-existing warnings (all `CS86xx` nullability + `SYSLIB0014` WebClient obsolescence — not introduced by PR #29) |
| `dotnet run Nolvus.Services.Tests` (console, `dotnet run`) | **6 / 6 PASS** — incl. `user-scoped credentials encrypt, persist, authenticate, and retain legacy read compatibility`, `settings preserve legacy credentials and round-trip newly protected Nexus keys`, `Linux credential keys persist through the current user's Secret Service` |
| `dotnet test Nolvus Dashboard Linux.sln` (xUnit) | **Nolvus.Package.Tests: 77 pass / 0 fail / 7 skipped** (skips are Windows-junction + Fluorine-platform tests that self-skip on Linux) |

These are representative (not acceptance): they prove the PR head compiles and its unit/integration tests pass on Linux, but they do **not** prove real-user Windows/Linux install, Nolvus/Mega/Nexus login, mod download, Mod Organizer, or backup/rollback.

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
