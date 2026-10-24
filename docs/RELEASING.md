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
6. Credential behavior and rollback limits are documented. New Nolvus/Mega/Nexus saves use platform-scoped protection, but legacy TripleDES Nolvus/Mega values and legacy plaintext Nexus values are only read-compatible until users explicitly save them again. Do not describe those unsaved legacy values as protected or claim automatic migration.
7. A supported real Nolvus test environment has completed applicable installer acceptance tests. Do not modify an ordinary user's game installation to satisfy this gate.
8. Release notes, known limitations, runtime requirements, and rollback instructions are accurate.

## Rollback

A release is a package, not an updater migration. If a draft is defective, keep it unpublished and attach a corrected build to a new version. For a published preview, mark it superseded or remove public availability through GitHub's normal release interface without rewriting tags or deleting history. Advise testers to close the app and remove only the newly extracted NKNOVA folder, preserving their game, modlist, Steam library, Proton prefix, and cache.

## ImageSharp security and licensing

The current dependency is ImageSharp 3.1.12 in six projects, with ImageSharp.Drawing 2.1.7 and ImageSharp.Web 3.2.0 in Nolvus.Services. Five advisories affect 3.1.12: `GHSA-gwg2-r3hj-4w44`, `GHSA-j3p4-wp97-rph4`, `GHSA-j9gm-c75j-xc9q`, `GHSA-jjfr-hcj7-qf5w`, and `GHSA-wmxv-xphr-5c9g`. The Sep 15, 2026 advisories list 4.1.2 as patched; 3.2.0 does not resolve the full set. Official releases list 4.1.3 as the latest release as of Oct 24, 2026.

The preserved `fix/imagesharp-security-update` stash changes direct package references to ImageSharp 4.1.3, Drawing 3.1.3, and Web 4.0.2. It is dependency-only and has not been applied, restored, compiled, or tested. ImageSharp types appear across core interfaces, services, package/report code, controls, and dashboard frames, so replacement is a broad API migration rather than a contained package swap. No replacement has been justified or tested.

Starting with ImageSharp 3.2.0, direct-reference projects require a valid Six Labors license at build time. Six Labors documents a community-key application path for open-source/source-available projects, requiring case-sensitive assembly names. NKNOVA appears to fit that category, but no application has been submitted and no key has been issued. The current licensing documentation says not to commit license files/keys to a public repository; the owner action is to submit the application and add the approved key to GitHub Actions as a repository secret such as `SIXLABORS_LICENSE_KEY`. The exact assemblies to provide are:

- `Vcc.Nolvus.Api.Installer`
- `Nolvus.Components`
- `Nolvus.Core`
- `NolvusDashboard`
- `Nolvus.Package`
- `Nolvus.Services` (also directly references ImageSharp.Drawing and ImageSharp.Web)

This is a release blocker until an authorized maintainer submits the community-license application, adds an approved key as a private GitHub Actions secret, and the exact candidate passes Linux/Windows restore, Release build, tests, and CI. Do not add a key to tracked files or suppress audit findings before that gate is satisfied. See [current ImageSharp license/build documentation](https://docs.sixlabors.com/articles/imagesharp/) and the [official v4.1.3 release](https://github.com/SixLabors/ImageSharp/releases/tag/v4.1.3).
