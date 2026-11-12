# NKNOVA Development Checkpoint

## Autonomous Development Status

- **Date:** 2026-11-12
- **Lead Autonomous Engineer:** Antigravity / Hermes Agent
- **Repository:** `ilievi187/NKNOVA-Dashboard-`
- **Base Branch:** `main`
- **Active Development Branch:** `task/initial-audit-and-checkpoint`

---

## 1. Initial Audit Summary

### 1.1 Architecture & Current Main Status
- **Current Main SHA:** `25c0701` (`security(credentials): use Windows DPAPI and Linux Secret Service (#29)`)
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
  - Alert 1: `cs/ecb-encryption` in `Nolvus.Services/Lib/LibService.cs` (eliminated in PR #29)
  - Alert 2: `cs/ecb-encryption` in `Nolvus.Services/Lib/LibService.cs` (eliminated in PR #29)
  - Alert 3: `cs/weak-encryption` TripleDES in `Nolvus.Services/Lib/LibService.cs` (eliminated in PR #29)
  - Alert 4: `cs/weak-encryption` TripleDES in `Nolvus.Services/Lib/LibService.cs` (eliminated in PR #29)
- **Credential Storage:**
  - Implemented `PlatformCredentialStore`: Windows Credential Manager with current-user DPAPI, Linux freedesktop Secret Service.
  - Opaque random reference keys (`nknova:ref:v4:...`) stored in settings INI; sensitive plaintext never touches INI files.
  - Fail-closed behavior on missing or locked Secret Service; atomic owner-only (0600) INI writing with symlink protection.
  - Full backwards compatibility with read-only v2/v3 AES-GCM envelopes and explicit user re-entry required (`LegacyCredentialReentryRequiredException`) for obsolete TripleDES credentials.
  - 100% CI pass across 17 GitHub Actions checks on PR #29 before squash merge into `main` (`25c0701`).

### 1.3 Outstanding Pull Requests Audit
- **PR #1 (`develop/linux-installer-v1`):**
  - Declarative Linux installer core (v0.1) based on older baseline.
  - Standalone `NKNOVA.Installer.Core`, `NKNOVA.Installer.Cli`, and `NKNOVA.Installer.Tests`.
  - Implements schemaVersion 1 JSON manifest, topological dependency sorter with cycle detection, read-only Linux probing for Steam/Proton/Fluorine/XWayland.
  - *Status:* Needs rebuilding / integration directly into `Nolvus Dashboard Linux.sln` under .NET 10 with verified download, transactional staging, and rollback support.
- **PR #2 (`feature/one-click-nolvus-go-enb`):**
  - One-click Nolvus v6 Graphics Only + ENB selector stacked on PR #1.
  - Implements ENB chooser (Cabbage, Kauz, PI-CHO, Amon), preflight verification of existing installation, and conservative graphics configuration.
  - *Status:* Dependent on PR #1 declarative core integration; requires explicit mutual exclusivity enforcement between Community Shaders and ENB.

---

## 2. Completed Tasks

| Task | Commit / PR | Verification | Status |
|------|-------------|--------------|--------|
| Rebase & sync PR #29 with `main` | `ee7f954` | All 17 CI checks passed, 19 hermetic auto-merge tests passed, 7 services regressions passed | Completed |
| Remediate CodeQL alerts 1-4 & merge PR #29 | `25c0701` (PR #29) | Squash merge with full CI verification and security gate compliance | Completed & Merged |
| Initial system & codebase audit | `task/initial-audit-and-checkpoint` | Local build and test run (.NET 10.0.402 SDK) | Completed |

---

## 3. Prioritized Development Backlog

### Phase B — Linux Declarative Installer Integration
1. **B1: Solution Integration of Declarative Installer Core**
   - Import and modernize `NKNOVA.Installer.Core` into `Nolvus Dashboard Linux.sln` targeting `net10.0`.
   - Wire unified Steam discovery to `Nolvus.Services.Game.GameService` for consistent multi-root library detection.
   - Add comprehensive unit tests in `Nolvus.Core.Tests` / `NKNOVA.Installer.Tests`.
2. **B2: Verified Downloads, Checksums, and Content Caching**
   - Implement SHA-256 integrity verification before extract/staging.
   - Implement content-addressed cache directory for downloaded archives.
   - Support resumable downloads and HTTP Range headers.
3. **B3: Transactional Installation and Rollback Engine**
   - Create install transaction journal (`journal.json`).
   - Atomic directory swapping / staging directories to prevent partial file corruption.
   - Rollback capability on interruption or step failure.

### Phase C — One-Click Nolvus v6 Graphics Only & Shader Profiles
1. **C1: Graphics Only Preflight & ENB Presets**
   - Port clean Nolvus v6 Graphics Only profile definitions.
   - Integrate ENB preset selector (Cabbage, Kauz, PI-CHO, Amon).
   - Ensure strict mutual exclusivity between ENB and Community Shaders.
2. **C2: Preflight Disk Space & Dependency Checks**
   - Accurate disk space estimation for base modlist and staging.
   - Preflight verification of clean stock Skyrim AE installation.

### Phase D — Dashboard Avalonia UI Improvements
1. **D1: Installer Flow & Progress Display**
   - Real-time progress bar with speed, ETA, and step breakdown.
   - Enhanced log summary and error diagnostics.

---

## 4. Known Blockers & Limitations

- **Live Skyrim Modification:** In accordance with instructions, testing is strictly performed against isolated test directories and mock roots; live end-user Skyrim installs are never modified.
- **ImageSharp Dependency:** ImageSharp 3.2.0 is pinned with vendor-issued assembly-scoped license; third-party package modifications remain locked under the dependency manifest gate.

---

## 5. Next Recommended Task

- **Task B1:** Rebuild and integrate the declarative installer core (`NKNOVA.Installer.Core`) and test suite into `Nolvus Dashboard Linux.sln`, unifying Steam/Proton discovery with existing services and verifying with automated regression tests.
