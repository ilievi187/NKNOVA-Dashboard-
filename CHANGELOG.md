# Change log

## 0.1.0-preview.2 (unpublished draft candidate)

This follow-up candidate supersedes the still-unpublished `v0.1.0-preview.1` draft. It focuses on remote image resource safety and does not change the existing ImageSharp package version.

### Included

- Limits remote image downloads to 16 MiB and 30 seconds, bounds decoded dimensions to 40 million pixels, and decodes only the first frame.
- Accepts PNG, JPEG, WebP, GIF, and BMP signatures. Unsupported TIFF and other formats are rejected before ImageSharp decoding.
- Routes account-avatar loading through the guarded image service on a background thread.
- Adds integration regressions using a local HTTP server for a valid PNG, rejected TIFF, oversized declared responses, and oversized streamed responses without `Content-Length`.

### Limitations

- No complete Nolvus installation was performed.
- ImageSharp 3.1.12 still requires an approved license before a patched package upgrade.
- Existing Nolvus/Mega stored passwords still use reversible legacy obfuscation.

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
- ImageSharp 3.1.12 remains in the dependency graph. Official advisories identify 3.2.0 as patched for affected v3 lines and 4.1.2 or later for affected v4 lines, but direct references require an approved Six Labors license at build time.
- Nolvus/Mega passwords continue to use inherited reversible client-side obfuscation with a built-in key. No user credential is bundled, but this is not secure at-rest protection. A backward-compatible OS/user-scoped credential migration remains necessary before stable release.
- Full end-user runtime, game, and modlist compatibility has not been established. See README and `docs/INSTALLATION.md`.
