# Change log

## Unreleased — Phase 2 security work (PR #29)

### Included

- Store new account credentials in Windows Credential Manager protected with current-user DPAPI or in Linux Secret Service; write only random v4 vault references into the INI.
- Preserve read-only compatibility for old v2/v3 AES-GCM envelopes and legacy plaintext Nexus keys. Reject fixed-key TripleDES Nolvus/Mega values and direct users to re-enter them.
- Make main-settings INI updates atomic and owner-only on Unix. Credential save/keyring work runs off the Avalonia UI thread and fails closed with an actionable error.
- Add regression coverage for credential round trips, persistence, tampering, missing/invalid keys, legacy reads, settings integration, symlink rejection, Linux keyring behavior, and test-only isolation.
- Run Linux Secret Service integration tests in an isolated D-Bus/GNOME Keyring session in CI.

### Remaining release blockers

- Existing legacy credentials are not decrypted; they remain in their original format until users explicitly re-enter and save them. Newly saved credentials use authenticated AES-GCM with Windows user-scoped DPAPI or Linux Secret Service key storage. Windows DPAPI still requires GitHub Windows CI verification.
- The legacy TripleDES-ECB decrypt path in `Nolvus.Services/Lib/LibService.cs` is removed as part of this credential reconciliation; the four `CodeQL` findings (`cs/weak-encryption`, `cs/ecb-encryption`) are remediated by removing the flagged code. Server-side CodeQL re-run on the PR head must confirm the alerts clear before merge. This does not establish real-user credential migration, a full GUI test, or an end-to-end Nolvus installation.
- No complete live Nolvus installation has been performed.

## 0.1.0-preview.3 (unpublished security preview draft)

- Upgrade direct ImageSharp references to security-maintained 3.2.0 under the vendor-issued assembly-scoped Six Labors Community license.
- Preserve NKNOVA's protected dependency review, cross-platform CI, CodeQL, and extracted-app startup smoke validation.
- Produce an unpublished Windows/Linux preview draft with SHA-256 artifacts after the release checks succeed.

This preview is **not stable**: a complete real Nolvus install has not been demonstrated; legacy Nolvus/Mega credential protection still needs migration. Do not overwrite an existing modlist or publish the draft publicly without an explicit maintainer release acceptance.

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
