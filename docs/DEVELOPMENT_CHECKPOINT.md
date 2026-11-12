# NKNOVA Development Checkpoint

## Autonomous Development Status

- **Date:** 2026-11-12
- **Lead Autonomous Engineer:** Antigravity / Hermes Agent
- **Repository:** `ilievi187/NKNOVA-Dashboard-`
- **Base Branch:** `main`
- **Active Development Branch:** `feature/declarative-installer-engine`

---

## 1. Initial Audit Summary

### 1.1 Architecture & Current Main Status
- **Current Main SHA:** `3097424` (`docs: establish autonomous development checkpoint and backlog (#39)`)
- **Runtime Environment:** .NET 10.0.402 SDK, C# 12 / .NET 10 runtime target.
- **Solution Configuration:** `Nolvus Dashboard Linux.sln` includes:
  - `Nolvus.Api.Library.Installer`
  - `Nolvus.Browser`
  - `Nolvus.Components`
  - `Nolvus.Core` & `Nolvus.Core.Tests`
  - `Nolvus.Dashboard` (Avalonia UI)
  - `Nolvus.Instance`
  - `Nolvus.NexusApi`
  - `Nolvus.Package` & `Nolvus.Package.Tests`
  - `Nolvus.Services` & `Nolvus.Services.Tests`
  - `Nolvus.StockGame`
  - `Nolvus.Updater`

### 1.2 Security & CodeQL Vulnerabilities (Phase A)
- **Resolved CodeQL Alerts:**
  - Alert 1: `cs/ecb-encryption` in `Nolvus.Services/Lib/LibService.cs` (FIXED on main)
  - Alert 2: `cs/ecb-encryption` in `Nolvus.Services/Lib/LibService.cs` (FIXED on main)
  - Alert 3: `cs/weak-encryption` TripleDES in `Nolvus.Services/Lib/LibService.cs` (FIXED on main)
  - Alert 4: `cs/weak-encryption` TripleDES in `Nolvus.Services/Lib/LibService.cs` (FIXED on main)
- **Code-Scanning Alert Count:** Exactly 0 open alerts on `ilievi187/NKNOVA-Dashboard-`.
- **Credential Storage:**
  - Implemented `PlatformCredentialStore`: Windows Credential Manager with current-user DPAPI, Linux freedesktop Secret Service.
  - Opaque random reference keys (`nknova:ref:v4:...`) stored in settings INI; sensitive plaintext never touches INI files.
  - Fail-closed behavior on missing or locked Secret Service; atomic owner-only (0600) INI writing with symlink protection.
  - Full backwards compatibility with read-only v2/v3 AES-GCM envelopes and explicit user re-entry required (`LegacyCredentialReentryRequiredException`) for obsolete TripleDES credentials.
  - 100% CI pass across 17 GitHub Actions checks on PR #29 before squash merge into `main` (`25c0701`).

### 1.3 Outstanding Pull Requests Audit
- **PR #1 (`develop/linux-installer-v1`):**
  - Declarative Linux installer core (v0.1) rebased, modernized for .NET 10, and integrated directly into `Nolvus.Core/Installer` without external dependencies.
- **PR #2 (`feature/one-click-nolvus-go-enb`):**
  - One-click Nolvus v6 Graphics Only + ENB selector stacked on PR #1.
  - Implements ENB chooser (Cabbage, Kauz, PI-CHO, Amon), preflight verification of existing installation, and conservative graphics configuration.
  - *Status:* ENB vs Community Shaders mutual exclusivity guard implemented in `CompatibilityRules.cs`.

---

## 2. Completed Tasks

| Task | Commit / PR | Verification | Status |
|------|-------------|--------------|--------|
| Rebase & sync PR #29 with `main` | `ee7f954` | All 17 CI checks passed, 19 hermetic auto-merge tests passed, 7 services regressions passed | Completed |
| Remediate CodeQL alerts 1-4 & merge PR #29 | `25c0701` (PR #29) | Squash merge with full CI verification and security gate compliance | Completed & Merged |
| Initial system & codebase audit | `3097424` (PR #39) | Local build and test run (.NET 10.0.402 SDK) | Completed & Merged |
| Phase B: Declarative Installer Engine (`NKNOVA.Installer.Core`) | `feature/declarative-installer-engine` | Manifest validation, topological cycle resolution, preflight checks, content cache, transactional staging, rollback | Completed |
| Phase B/C: Shader Compatibility & Preflight Rules | `feature/declarative-installer-engine` | Mutual exclusivity between ENB and Community Shaders, disk space preflight checks | Completed |
| Automated Unit & Integration Tests | `feature/declarative-installer-engine` | 40 passed in `Nolvus.Core.Tests`, 77 passed in `Nolvus.Package.Tests`, 7 passed in services regressions | Completed |

---

## 3. Prioritized Development Backlog

### Phase B — Linux Declarative Installer Integration (In Progress)
- [x] B1: Core Declarative Engine (`Manifest.cs`, `Preflight.cs`, `ManifestValidation.cs`)
- [x] B2: Cryptographic Content Cache & Verified Streaming (`ContentCache.cs`)
- [x] B3: Transactional Installation Journal & Atomic Rollback (`InstallTransaction.cs`)
- [x] B4: Dependency Cycle Resolution & Topological Sorter
- [ ] B5: Integration of declarative preflight into Dashboard UI / CLI command

### Phase C — One-Click Nolvus v6 Graphics Only & Shader Profiles
- [x] C1: Shader Compatibility Engine (`CompatibilityRules.cs`) enforcing strict mutual exclusivity between ENB presets (Cabbage, Kauz, PI-CHO, Amon) and Community Shaders
- [x] C2: Preflight Disk Space Checks (`CompatibilityRules.CheckDiskSpace`)
- [ ] C3: One-click profile wizard binding for Avalonia GUI

### Phase D — Dashboard Avalonia UI Improvements
- [ ] D1: Real-time installation progress bar, speed, and step reporting
- [ ] D2: Error diagnostics and journal log viewer

---

## 4. Known Blockers & Limitations

- **Live Skyrim Modification:** In accordance with instructions, testing is strictly performed against isolated test directories and mock roots; live end-user Skyrim installs are never modified.
- **ImageSharp Dependency:** ImageSharp 3.2.0 is pinned with vendor-issued assembly-scoped license; third-party package modifications remain locked under the dependency manifest gate.

---

## 5. Next Recommended Task

- Merge `feature/declarative-installer-engine` via PR after full CI verification.
- Proceed to Phase C3 / Phase D1: Wire the declarative preflight plan and progress reporting into the Avalonia UI wizard frames.
