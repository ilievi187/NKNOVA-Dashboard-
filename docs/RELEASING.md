# Release process

## Scope and trust boundary

The `NKNOVA Preview Release Validation` workflow is the release packaging workflow for this candidate. On relevant pull requests it runs solution and focused tests, builds Linux x64 and Windows x64 packages on native GitHub-hosted runners, validates archive contents and SHA-256 files, and smoke-tests each extracted application. On version tags, it creates an unpublished GitHub draft pre-release after both platform jobs pass.

A draft is not a stable release or proof that a full Nolvus installation works. Do not publish the draft publicly while any release-blocking security, licensing, or real-install validation gate remains unresolved.

## Versioning and tagging

The Dashboard project version is the source of truth. A release tag must exactly match it with a `v` prefix, for example `v0.1.0-preview.2` for `<Version>0.1.0-preview.2</Version>`. The workflow checks that the tagged commit is an ancestor of `main`, refuses to overwrite an existing release, and creates a draft pre-release only after both platform jobs pass. The `v0.1.0-preview.1` draft remains unpublished and is superseded by later preview tags.

Only tag a commit already on protected `main` after its pull request and required checks have passed. Never force-push or retarget a release tag. If a tagged build fails, leave the failed run as evidence and issue a new patch preview tag after fixing the cause.

## Package policy

- Linux and Windows packages contain the Dashboard only. The inherited upstream updater is intentionally not packaged or launched.
- No game or modlist data, credentials, or private profile data may be included.
- Archive validation rejects unsafe paths, debug symbols, updater binaries, and common Skyrim game/mod asset extensions.
- Archive creation sorts entries and normalizes timestamps/owners. CI re-packs the extracted Linux and Windows payloads and compares SHA-256 hashes to check archive-layer reproducibility. This does not prove byte-identical recompilation across SDK or runner versions.
- Build jobs generate SHA-256 checksums; the draft creation job verifies them again after downloading the uploaded GitHub Release assets.
- Packages are framework-dependent and require the .NET 10 runtime.

## Required review before publishing

Before changing the draft to a public pre-release, a maintainer must verify all of the following:

1. Required Linux, Windows, and security checks passed on the exact tag commit.
2. Uploaded asset hashes match the CI-generated packages.
3. No unresolved critical/high findings affect shipped behavior; any finding dismissed as unreachable must be mapped to the actual call graph and reviewed before closure.
4. The ImageSharp advisories have been fixed with a compatible licensed package or a reachable-code mitigation has been independently reviewed and documented.
5. A valid approved Six Labors license is available for every direct-reference assembly before building upgraded packages.
6. Legacy Nolvus/Mega credential storage has either been migrated compatibly to platform/user-scoped protection or its residual risk is explicitly accepted for the preview. Do not describe the existing fixed-key obfuscation as secure storage.
7. A supported real Nolvus test environment has completed applicable installer acceptance tests. Do not modify an ordinary user's game installation to satisfy this gate.
8. Release notes, known limitations, runtime requirements, and rollback instructions are accurate.

## Rollback

A release is a package, not an updater migration. If a draft is defective, keep it unpublished and attach a corrected build to a new version. For a published preview, mark it superseded or remove public availability through GitHub's normal release interface without rewriting tags or deleting history. Advise testers to close the app and remove only the newly extracted NKNOVA folder, preserving their game, modlist, Steam library, Proton prefix, and cache.

## ImageSharp security and licensing

Do not suppress NuGet audit findings or add a key before approval. ImageSharp 3.1.12 is in the affected ranges of multiple current advisories. Official Six Labors advisories identify 3.2.0 as patched for affected v3 lines and 4.1.2 or later for affected v4 lines. The application is currently on 3.1.12; the preserved `fix/imagesharp-security-update` stash contains a proposed 4.1.3 dependency change, but that candidate has not been validated with a license and remains untouched.

Direct package references to ImageSharp 3.2.0 and later require a Six Labors license at build time. The official application page explicitly accepts open-source/source-available projects and states eligible open-source applications receive a community key automatically. NKNOVA's public GPL-3.0 repository appears to fit that category, but no application has been submitted and no key has been issued or added. The six current direct-reference assembly names to provide are:

- `Vcc.Nolvus.Api.Installer`
- `Nolvus.Components`
- `Nolvus.Core`
- `NolvusDashboard`
- `Nolvus.Package`
- `Nolvus.Services` (also directly references ImageSharp.Drawing and ImageSharp.Web)

The names are case-sensitive. The official docs say an assembly-scoped community key may be committed to the named public project; unrestricted/community keys and commercial keys must remain private. Maintainers must not add an unapproved key or treat this release gate as satisfied until the approved license is in place and all direct-reference builds pass.
