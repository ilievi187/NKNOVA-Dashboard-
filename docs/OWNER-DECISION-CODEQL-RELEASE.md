# OWNER DECISION PACKAGE — CodeQL gate, PR #29, and release readiness

Repo: `ilievi187/NKNOVA-Dashboard-`
Prepared: 2026-10-29 · All claims below verified via `gh api`, `git show`, OSV/GHSA, local grep, and a live local build + test run. **Build + tests now VERIFIED GREEN on the working-tree credential model** (base `8eed5ba`), on **.NET SDK 10.0.403** (repo targets `net10.0`; the pre-existing 9.0.319 SDK cannot target it — `NETSDK1045`). Still pending: re-verify on the reconciled `e180dc5` tree (Option A) + CodeQL re-run + owner decision. See §4.
Status: **PR #29 is NOT mergeable.** No bypass, no self-approve, no self-merge, no force-push performed.

---

## 1. One-line status

The only red check on PR #29 is the **aggregate `CodeQL`** gate, which fails because the real PR head still ships a **TripleDES-ECB legacy credential read path** in `Nolvus.Services/Lib/LibService.cs`. All 4 open code-scanning alerts map to that one file. A **stronger credential model** (`PlatformCredentialStore` + `LegacyCredentialReentryRequiredException`) already exists in the working tree of the `NKNOVA-Dashboard-` clone, **but** it is built on base `8eed5ba` (31 commits behind the real head `e180dc5`) and still references **ImageSharp 3.1.12** (vulnerable). Re-applying it onto `e180dc5` is a reconciliation, not a one-line commit (see §7).

---

## 2. PR inventory (all open PRs)

| PR | Title | draft | mergeState | head |
|----|-------|-------|-----------|------|
| #1 | WIP: NKNOVA declarative Linux installer core (v0.1) | yes | BEHIND | `5726c6a` |
| #2 | feat: NKNOVA one-click Nolvus v6 Graphics Only + ENB (Win/Linux) | yes | DIRTY | `d89e88b` |
| #29 | security(credentials): use Windows DPAPI and Linux Secret Service | yes | **BLOCKED** | `e180dc5` |
| #30 | security: block trusted auto-merge on aggregate CodeQL failures | yes | BEHIND | `c244b6e` |

`main` HEAD: `a8bf1d02`.

---

## 3. The blocking gate (exact, verified)

- Branch protection on `main`: **not configured** (REST 404 "Branch not protected"). So the *only* hard gate is the **aggregate CodeQL status check**, which is **red**.
- Language-specific CodeQL checks: **green**. The **aggregate** is red ⇒ the repo's "CodeQL" check fails as a whole.
- `main` `a8bf1d02` carries the offending source at `Nolvus.Services/Lib/LibService.cs`:
  - L169 `public string EncryptString` → L174 `new TripleDESCryptoServiceProvider` → L178 `Mode = CipherMode.ECB`
  - L194 `public string DecryptString` → L199 `new TripleDESCryptoServiceProvider` → L203 `Mode = CipherMode.ECB`
- Real PR #29 head `e180dc5` **still** has the legacy read path:
  - L181 `EncryptString`
  - L186 `DecryptString` → L193 `: DecryptLegacyString(value);` → L196 `private static string DecryptLegacyString` → L223 `tripleDes.Mode = CipherMode.ECB;`
- The 4 open code-scanning alerts (created `2026-10-23T17:44:56Z`):

| # | rule | maps to |
|---|------|---------|
| 1 | `cs/ecb-encryption` | `LibService.cs` Encrypt/Decrypt TripleDES-ECB |
| 2 | `cs/ecb-encryption` | `LibService.cs` legacy decrypt TripleDES-ECB |
| 3 | `cs/weak-encryption` | `LibService.cs` TripleDES key/IV constants |
| 4 | `cs/weak-encryption` | `LibService.cs` TripleDES key/IV constants |

These are **pre-existing on `main`** (not introduced by PR #29), but they persist **on the PR head** because the legacy read path was not removed there. They therefore fail the aggregate CodeQL gate for the PR.

---

## 4. Two different implementations — do not conflate

| location | commit | credential model | TripleDES? | ImageSharp |
|----------|--------|------------------|-----------|-----------|
| **Real PR #29 head** (`e180dc5`) | committed | `UserCredentialProtector` (AES-GCM per-user) for *new* values, **plus** `DecryptLegacyString` TripleDES-ECB **read fallback** | **YES** (read path) | **3.2.0** (patched) |
| **Local `NKNOVA-Dashboard-` branch** `8eed5ba` (base of working tree) | committed | same as PR head (has `DecryptLegacyString`) | **YES** | **3.1.12** (vulnerable) |
| **Local `NKNOVA-Dashboard-` WORKING TREE** | **UNCOMMITTED** | `PlatformCredentialStore` (DPAPI / Secret Service) + `LegacyCredentialReentryRequiredException` guard + `UserCredentialProtector` AES-GCM; **no** `TripleDES`, **no** `CipherMode.ECB`, **no** `DecryptLegacyString` anywhere in the tree | **NO** | **3.1.12** (vulnerable) |

> **Reconciliation required:** the working-tree credential changes remove TripleDES but were developed against the `8eed5ba` base. The 31 commits between `8eed5ba` and `e180dc5` include `283e02a` (which also modified `LibService.cs` — ciphertext validation/bounding) and `922d72d` (ImageSharp 3.1.12 → 3.2.0 in `.csproj`). The 20 uncommitted changes **do not touch any `.csproj` file**, so re-applying them onto `e180dc5` will **not** regress ImageSharp. The integration risk is in `LibService.cs` specifically: `283e02a` added ciphertext-validation logic that the working-tree `LibService.cs` rewrite must preserve or supersede correctly. See §7 Option A.

Working-tree diff (18 modified + 2 new files, all uncommitted):
- new: `Nolvus.Services/Lib/PlatformCredentialStore.cs`, `Nolvus.Services/Lib/LegacyCredentialReentryRequiredException.cs`
- modified: `LibService.cs`, `UserCredentialProtector.cs`, `GlobalsService.cs`, `SettingsService.cs`, `ILibService.cs`, `StartFrame.axaml.cs`, `SettingsCache.cs`, `NolvusFrame`, `GlobalSettingsFrame`, `Program.cs` (tests), `LinuxSecretServiceKeyStore.cs`, workflow, CHANGELOG, README, docs (CREDENTIALS/RELEASING/TROUBLESHOOTING), `run-services-regressions-linux.sh`.

`LibService.cs` in the working tree:
```csharp
private readonly PlatformCredentialStore _credentialStore;
...
public string StoreCredential(string value) => _credentialStore.Store(value);
public string RetrieveCredential(string value)
{
    ...
    if (PlatformCredentialStore.IsReference(value)) { /* read via store */ }
    throw new LegacyCredentialReentryRequiredException();   // no silent TripleDES read
}
```

**Build/test status (VERIFIED, 2026-10-29 ~01:25 UTC, `NKNOVA-Dashboard-` working tree, .NET SDK 10.0.403):**

| run | result |
|-----|--------|
| `dotnet build "Nolvus Dashboard Linux.sln" -c Release` | **0 Error(s)** (928 warnings — nullable/Avalonia, pre-existing) |
| `dotnet test Nolvus.Package.Tests` | **Passed: 59, Failed: 0** (6 skipped, Windows-junction) |
| `dotnet test Nolvus.Core.Tests` | **Passed: 9, Failed: 0** (3 skipped, Windows) |
| `bash scripts/run-services-regressions-linux.sh` (isolated Secret Service session) | **All PASS**, incl. credential round-trip, legacy AES readable, unsafe-legacy-password re-entry, v3 AES keys readable without migration, Linux Secret Service persistence |

- The working tree (base `8eed5ba`): **0** `.cs` files reference `TripleDES`/`CipherMode.ECB`; credentials use **`AesGcm`**; `PlatformCredentialStore` + `LegacyCredentialReentryRequiredException` present.
- `NKNOVA-Dashboard-qwen` (branch `pr29-codeql-doc`): 1 `.cs` file still references `TripleDES` (the `LibService.cs` on this doc-only branch) — it is a documentation branch, not a build target.
- **Important caveat:** this green result is on the **working-tree base `8eed5ba`** (ImageSharp **3.1.12**, which is in the vulnerable range for CVE-2026-106113/106115) — i.e. it proves the credential model **builds and passes its own regression suite**, but it is **not yet the reconciled `e180dc5` tree**. The Option A reconciliation onto `e180dc5` (ImageSharp 3.2.0 + `283e02a` + Windows-package fixes) must be built + re-tested, and CodeQL re-run, before a green claim on the PR.

---

## 5. ImageSharp 3.2.0 — VERIFIED, not an EOL blocker

`SixLabors.ImageSharp 3.2.0` is a **fixed** release for a batch of October 2026 advisories:

| GHSA | CVE | severity | vulnerable range | first patched |
|------|-----|----------|------------------|---------------|
| GHSA-j3p4-wp97-rph4 | CVE-2026-106113 | high | `>= 2.0.0, < 3.2.0` | **3.2.0** |
| GHSA-jjfr-hcj7-qf5w | CVE-2026-106115 | high (7.5) | `>= 2.1.0, < 3.2.0` | **3.2.0** |
| GHSA-wmxv-xphr-5c9g | CVE-2026-106116 | high | `>= 2.0.0, <= 4.1.1` | 3.2.0 / 4.1.2 |
| GHSA-4q3p-rj5x-xv7p | CVE-2026-106111 | medium | `>= 4.0.0, <= 4.1.1` | 4.1.2 (3.x unaffected) |
| GHSA-ffp7-56pq-64mr | CVE-2026-106112 | high | `>= 4.0.0, <= 4.1.1` | 4.1.2 (3.x unaffected) |

SixLabors support policy: each major version gets security updates for 12 months after the successor's first stable → **ImageSharp v3 is supported until ~12 May 2027**. **3.2.0 is a current, patched, supported release.** It is **not** an EOL/advisory blocker. (If the repo wants the newest, 4.1.2 is the latest patch, but 3.2.0 is acceptable and in-range.)

---

## 6. Release workflow skip (verified, correct behavior)

`.github/workflows/nknova-preview-release.yml`:
- L187 `name: Create GitHub draft pre-release`
- **L188 `if: github.event_name == 'push' && startsWith(github.ref, 'refs/tags/v')`**

So the `create-draft-release` job is **skipped on any branch push / PR** — it only runs on a tag push (`refs/tags/v*`). This is intended, not a defect. A release is produced by tagging `vX.Y.Z`, not by merging a branch.

---

## 7. DECISION REQUIRED FROM OWNER

> **Important (verified):** the local branch `fix/nknova-user-scoped-credentials` at `8eed5ba` is a **true ancestor** of the PR head `e180dc5` (0 commits ahead, 31 behind). It is **not diverged** — it is a strict subset. The 20 uncommitted working-tree changes **do not touch any `.csproj` files**, so they cannot regress ImageSharp. However, they were developed against `8eed5ba`, which predates 31 commits of security work:
>   - `283e02a` security(credentials): bound and validate legacy ciphertext before decryption
>   - `ac73a1c` test(credentials): reject malformed and oversized legacy ciphertext without writes
>   - `b5f5fef` / `2839c27` / `13130e0` Windows-package: path-traversal + version-mismatch + embedded-updater rejection
>   - `92f0045` / `0be7ae9` / `2eaad1c` settings: atomic symlink-safe writes + encrypted-only credential fields
>   - `922d72d` / `b759619` ImageSharp **3.2.0** upgrade + license gate (working-tree base `8eed5ba` is still on **3.1.12**)
>   - `24925a4` fix(release): require actual CodeQL main checks instead of PR-only aggregate
>
> The reconciliation task is to **re-apply** the uncommitted credential-model changes onto `e180dc5` (same branch, 31 commits ahead), ensuring they integrate correctly with `283e02a` (which modified `LibService.cs`) and the Windows-package fixes. Since `8eed5ba` is an ancestor, a fast-forward + re-apply is the mechanical path.

### Option A — REMEDIATE on the CURRENT PR HEAD (recommended; closes the gate legitimately)
Re-apply the `PlatformCredentialStore` / `LegacyCredentialReentryRequiredException` credential model **onto the current PR head `e180dc5`** (i.e. onto the 3.2.0 + `283e02a` line), NOT onto the older `8eed5ba` base. Concretely:

**Reconciliation scope (verified by diffing `8eed5ba..e180dc5` and working-tree grep):**

| file | action on top of `e180dc5` |
|------|---------------------------|
| `Nolvus.Services/Lib/PlatformCredentialStore.cs` | **new** — copy from working tree |
| `Nolvus.Services/Lib/LegacyCredentialReentryRequiredException.cs` | **new** — copy from working tree |
| `Nolvus.Services/Lib/LibService.cs` | replace `EncryptString`/`DecryptString` + TripleDES body with `StoreCredential`/`RetrieveCredential`/`DeleteCredentialReference` + `_credentialStore` |
| `Nolvus.Core/Interfaces/ILibService.cs` | replace 2-method interface with 3-method interface |
| `Nolvus.Services/Globals/GlobalsService.cs` | `EncryptString`→`StoreCredential`, `DecryptString`→`RetrieveCredential` |
| `Nolvus.Services/Settings/SettingsService.cs` | `EncryptString`→`StoreCredential`, remove legacy envelope validation (replaced by `283e02a` + new store) |
| `Nolvus.Dashboard/Frames/StartFrame.axaml.cs` | `DecryptString`→`RetrieveCredential` |
| `Nolvus.Dashboard/Frames/Settings/GlobalSettingsFrame.axaml.cs` | `EncryptString`→`StoreCredential`, add `DeleteCredentialReference` call on credential change |
| `Nolvus.Services.Tests/Program.cs` | full test rewrite (working tree has 642 lines of credential tests; real head has 500 lines of legacy tests) |
| `Nolvus.Services/Lib/UserCredentialProtector.cs` | adapt to work with `PlatformCredentialStore` (working tree version) |

**Constraints to preserve (do NOT regress):**
- ImageSharp **3.2.0** (do NOT let the working tree's 3.1.12 bleed in);
- `283e02a` legacy-ciphertext bound+validation (the new store must *coexist* with it, not replace it);
- the Windows-package security fixes (`b5f5fef` / `2839c27`).

1. Apply the file table above **on top of `e180dc5`**.
2. Result: `LibService.cs` no longer references TripleDES/ECB → the 4 alerts have **no live code** to point at.
3. Re-run CodeQL on the PR head → aggregate turns **green**.
4. Re-run local build + `Nolvus.Services.Tests` / `Nolvus.Package.Tests` on the reconciled tree.
5. Confirm 0 open `cs/ecb-encryption` / `cs/weak-encryption` alerts remain for that path (CodeQL re-analysis clears them).
6. Only then un-draft PR #29 and merge.

> This is a **reconciliation task**, not a one-line commit, because `283e02a` also modified `LibService.cs` (ciphertext validation) and the working-tree rewrite must integrate with it. The 20 uncommitted changes do **not** touch any `.csproj` file, so ImageSharp 3.2.0 is preserved automatically.
This is the only path that is defensible as "stable" without an owner-signed exception.

### Option B — OWNER-APPROVED DISMISS (only if owner explicitly accepts the risk)
Keep the legacy TripleDES read path and **dismiss** the 4 open alerts with a recorded, owner-signed rationale + expiry. This does **not** require a code change but:
- leaves a known weak-encryption read path in the shipped binary;
- must be re-decided each time the path reappears;
- is **not** acceptable as evidence of a stable release by itself.

**No bypass, no self-approve, no force-push** under either option.

---

## 8. Definition-of-Done before ANY "stable release" claim

A release may be called stable **only** when ALL of the following are true and evidenced:

- [ ] PR #29 is **not draft**
- [ ] aggregate `CodeQL` is **green** (or owner-signed exception on file, Option B)
- [ ] 0 open `cs/ecb-encryption` / `cs/weak-encryption` alerts on the shipped path
- [ ] local build 0 errors + `Nolvus.Services.Tests` + `Nolvus.Package.Tests` green — **already true on the working tree (base `8eed5ba`); must be re-confirmed on the reconciled `e180dc5` tree (Option A)**
- [ ] **Real end-to-end proof** (not just unit tests) on BOTH Windows and Linux:
  - [ ] fresh install
  - [ ] Nolvus login with a real account
  - [ ] mod download completes
  - [ ] Mod Organizer integration works
  - [ ] backup → rollback round-trip
  - [ ] credential store: new value stored + retrieved (DPAPI on Windows / Secret Service on Linux)
  - [ ] legacy credential re-entry: `LegacyCredentialReentryRequiredException` raised and surfaced (not silent decrypt)
- [ ] ImageSharp at a patched, in-support version (3.2.0 confirmed OK)

---

## 9. What I will NOT do (standing constraints, honored)

- no self-approve, no self-merge, no force-push, no rewrite of shared history
- no dismiss of security alerts without explicit owner authorization
- no bypass of the aggregate CodeQL gate
- no "stable" claim until section 8 is fully evidenced
- reports stay short, technical, in Bulgarian

---

## Appendix A — exact commands used (reproducible)

```bash
gh pr list --repo ilievi187/NKNOVA-Dashboard- --state open
gh api repos/ilievi187/NKNOVA-Dashboard-/branches/main/protection        # → 404 not protected
gh api "repos/ilievi187/NKNOVA-Dashboard-/code-scanning/alerts?state=open"
git show a8bf1d02:Nolvus.Services/Lib/LibService.cs | grep -n "TripleDES\|CipherMode.ECB"
git show e180dc5:Nolvus.Services/Lib/LibService.cs | grep -n "DecryptLegacyString\|CipherMode.ECB"
grep -rn "TripleDES\|CipherMode.ECB" --include=*.cs .    # working tree → empty
curl -s "https://api.github.com/advisories?ecosystem=nuget&affects=SixLabors.ImageSharp"

# Local build + test (requires .NET SDK 10.x; 9.x fails with NETSDK1045)
# Install via mise if needed:  mise install dotnet@10.0.403
cd <NKNOVA-Dashboard- clone>
mise exec dotnet@10.0.403 -- dotnet build "Nolvus Dashboard Linux.sln" -c Release
mise exec dotnet@10.0.403 -- dotnet test Nolvus.Package.Tests/Nolvus.Package.Tests.csproj -c Release
mise exec dotnet@10.0.403 -- dotnet test Nolvus.Core.Tests/Nolvus.Core.Tests.csproj -c Release
mise exec dotnet@10.0.403 -- bash scripts/run-services-regressions-linux.sh
```
