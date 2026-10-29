# OWNER DECISION PACKAGE — CodeQL gate, PR #29, and release readiness

Repo: `ilievi187/NKNOVA-Dashboard-`
Prepared: 2026-10-29 · All claims below verified via `gh api`, `git show`, OSV/GHSA, local build+test.
Status: **PR #29 is NOT mergeable.** No bypass, no self-approve, no self-merge, no force-push performed.

---

## 1. One-line status

The only red check on PR #29 is the **aggregate `CodeQL`** gate, which fails because the real PR head still ships a **TripleDES-ECB legacy credential read path** in `Nolvus.Services/Lib/LibService.cs`. All 4 open code-scanning alerts map to that one file. A **stronger, cleaner remediation already exists but is uncommitted** (working tree of the `NKNOVA-Dashboard-` clone) and is therefore **not on the PR**.

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

| location | commit | credential model | TripleDES? |
|----------|--------|------------------|-----------|
| **Real PR #29 head** (`e180dc5`) | committed | `UserCredentialProtector` (AES-GCM per-user) for *new* values, **plus** `DecryptLegacyString` TripleDES-ECB **read fallback** | **YES** (read path) |
| **Local `NKNOVA-Dashboard-` branch** `8eed5ba` | committed | same as PR head (has `DecryptLegacyString`) | **YES** |
| **Local `NKNOVA-Dashboard-` WORKING TREE** | **UNCOMMITTED** | `PlatformCredentialStore` (DPAPI / Secret Service) + `LegacyCredentialReentryRequiredException` guard + `UserCredentialProtector` AES-GCM; **no** `TripleDES`, **no** `CipherMode.ECB`, **no** `DecryptLegacyString` anywhere in the tree | **NO** |

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

**Local build + test (qwen copy, earlier):** 0 build errors; `Nolvus.Services.Tests` 6/6; `Nolvus.Package.Tests` 77 pass / 0 fail / 7 skipped. (Re-run against the committed remediation before asserting.)

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

### Option A — REMEDIATE (recommended; closes the gate legitimately)
Commit the already-built working-tree remediation (DPAPI / Secret Service + AES-GCM + `LegacyCredentialReentryRequiredException`) onto the real PR #29 branch, removing the TripleDES-ECB read path. Then:
1. `Nolvus.Services/Lib/LibService.cs` no longer references TripleDES/ECB → the 4 alerts have **no live code** to point at.
2. Re-run CodeQL on the PR head → aggregate turns **green**.
3. Re-run local build + `Nolvus.Services.Tests` / `Nolvus.Package.Tests`.
4. Confirm no `cs/ecb-encryption` / `cs/weak-encryption` open alerts remain for that path (CodeQL re-analysis clears them).
5. Only then un-draft PR #29 and merge.

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
- [ ] local build 0 errors + `Nolvus.Services.Tests` + `Nolvus.Package.Tests` green
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
```
