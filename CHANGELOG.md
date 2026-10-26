# Change log

## Unreleased — Phase 2 security work

### Included

- Protect newly saved account credentials with AES-GCM. Windows stores the key using current-user DPAPI; Linux stores it through the user's freedesktop Secret Service, with no plaintext-file fallback.
- Preserve read-only compatibility for legacy TripleDES Nolvus/Mega values, plaintext Nexus keys, and the prior v2 AES-GCM file-key envelope. Reads do not silently rewrite credentials.
- Make main-settings INI updates atomic and owner-only on Unix. Credential save/keyring work runs off the Avalonia UI thread and fails closed with an actionable error.
- Add regression coverage for credential round trips, persistence, tampering, missing/invalid keys, legacy reads, settings integration, symlink rejection, Linux keyring behavior, and test-only isolation.
- Run Linux Secret Service integration tests in an isolated D-Bus/GNOME Keyring session in CI.

### Remaining release blockers

- Existing legacy credentials remain in their original format until users explicitly re-enter and save them. As of 2026-10-26, PR #29 commit `ac73a1c` passed NKNOVA CI, ExecutableResolver CI, Security Gates, and Preview Release Validation, including Windows/Linux packaging and startup smoke jobs. Four synthetic regressions now reject malformed or oversized legacy ciphertext. This does not establish real-user credential migration, a full GUI test, or an end-to-end Nolvus installation.
- On 2026-10-26, at PR #29 commit `ac73a1c`, the GitHub Advanced Security aggregate `CodeQL` check remains **failure**, despite successful `CodeQL (csharp)`, `CodeQL (actions)`, and `CodeQL (rust)` jobs. The unresolved TripleDES-ECB finding in `Nolvus.Services/Lib/LibService.cs` is not remediated by input validation. A successful analysis job must never be used to dismiss the security finding.
- Draft PR #30 proposes a fail-closed auto-merge gate for the aggregate `CodeQL` result and verifies the reporting GitHub App. It requires manual review, passing checks, and safe merge before this protection is active on `main`.
- ImageSharp 3.1.12 remains affected by five current advisories. The preserved v4.1.3 dependency candidate needs an approved Six Labors community key in a private CI secret and cross-platform validation before it can be applied.
- No complete live Nolvus installation has been performed.

## 0.1.0-preview.2 (unpublished draft candidate)

This follow-up candidate supersedes the still-unpublished `v0.1.0-preview.1` draft. It focuses on remote image resource safety and does not change the existing ImageSharp package version.

### Included

- Limits remote image downloads to 16 MiB and 30 seconds, bounds decoded dimensions to 40 million pixels, and decodes only the first frame.
- Accepts PNG, JPEG, WebP, GIF, and BMP signatures. Unsupported TIFF and other formats are rejected before ImageSharp decoding.
- Moves account-avatar and error-summary remote image loading off the UI thread; error thumbnails are bounded to two concurrent loads.
- Adds integration regressions using a local HTTP server for a valid PNG, rejected TIFF, oversized declared responses, and oversized streamed responses without `Content-Length`.

### Limitations

- No complete Nolvus installation was performed.
- ImageSharp 3.1.12 still requires an approved license before a patched package upgrade.
- Existing unsaved Nolvus/Mega values still use reversible legacy obfuscation; new values use user-scoped protection.

## 0.1.0-preview.1 (unpublished draft)

This is the first NKNOVA preview candidate. It is an unpublished draft for maintainer validation, not a stable or public release.

### Included

- Existing Avalonia Nolvus Dashboard application packaged for Linux x64 and Windows x64.
- .NET 10 target and version metadata for the preview candidate.
- Steam library discovery regression coverage, including secondary Steam libraries and rejection of an incomplete game directory.
- Safer Linux and Windows packaging checks, SHA-256 checksums, archive path/content validation, and extracted-app startup smoke tests.
- Disabled the inherited upstream self-update path so NKNOVA cannot launch the upstream updater and replace itself unexpectedly.

### Limitations and unresolved items

- No complete live Nolvus download or installation was performed.
- Windows Nolvus installation and recovery remain unverified end to end.
- ImageSharp 3.1.12 remains in the dependency graph. The Sep 15, 2026 advisory set lists 4.1.2 as patched; v3.2.0 does not fix the full current set. Direct references require a valid Six Labors license at build time.
- New credentials use Windows current-user DPAPI or Linux Secret Service key storage. Existing Nolvus/Mega/Nexus credentials are read-compatible but are not silently migrated; old values remain at their prior risk until explicitly saved again.
- Full end-user runtime, game, and modlist compatibility has not been established. See README and `docs/INSTALLATION.md`.
