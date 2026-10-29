# Release process

## Scope and trust boundary

The `NKNOVA Preview Release Validation` workflow is the release packaging workflow for this candidate. On relevant pull requests it runs solution and focused tests, builds Linux x64 and Windows x64 packages on native GitHub-hosted runners, validates archive contents and SHA-256 files, and smoke-tests each extracted application. On a protected main-branch version bump, it creates an unpublished draft pre-release after both platform jobs pass and the same main commit has successful aggregate C# and Actions CodeQL. Tags also remain supported for a manual release candidate.

A draft is not a stable release or proof that a full Nolvus installation works. Do not publish the draft publicly while any release-blocking security, licensing, or real-install validation gate remains unresolved.

## Versioning and tagging

The Dashboard project version is the source of truth. A release tag must exactly match it with a `v` prefix, for example `v0.1.0-preview.3` for `<Version>0.1.0-preview.3</Version>`. The workflow checks that the tagged commit is an ancestor of `main`, refuses to overwrite an existing release, and creates a draft pre-release only after both platform jobs pass. The `v0.1.0-preview.1` draft remains unpublished and is superseded by later preview tags.

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

1. Required Linux, Windows, and security checks passed on the exact tag commit. In particular, verify the **aggregate `CodeQL` check reported by the `github-advanced-security` GitHub App** is `success` on that exact commit, not merely the `CodeQL (csharp)` / `CodeQL (actions)` / `CodeQL (rust)` workflow jobs. Confirm that the active repository code-scanning ruleset has no unresolved blocking findings; never dismiss a genuine finding to make a release pass.
2. Uploaded asset hashes match the CI-generated packages.
3. No unresolved critical/high findings affect shipped behavior; any finding dismissed as unreachable must be mapped to the actual call graph and reviewed before closure.
4. The ImageSharp advisories have been fixed with a compatible licensed package or a reachable-code mitigation has been independently reviewed and documented.
5. A valid approved Six Labors license is available for every direct-reference assembly before building upgraded packages.
6. Credential behavior and rollback limits are documented. New Nolvus/Mega/Nexus saves use platform-vault references. v2/v3 AES-GCM values remain read-only compatible, while fixed-key TripleDES Nolvus/Mega values are rejected and require user re-entry; legacy plaintext Nexus keys remain readable until explicitly re-saved. Do not describe unsupported legacy passwords as protected.
7. A supported real Nolvus test environment has completed applicable installer acceptance tests. Do not modify an ordinary user's game installation to satisfy this gate.
8. Release notes, known limitations, runtime requirements, and rollback instructions are accurate.

## Rollback

A release is a package, not an updater migration. If a draft is defective, keep it unpublished and attach a corrected build to a new version. For a published preview, mark it superseded or remove public availability through GitHub's normal release interface without rewriting tags or deleting history. Advise testers to close the app and remove only the newly extracted NKNOVA folder, preserving their game, modlist, Steam library, Proton prefix, and cache.

## ImageSharp security and licensing

NKNOVA now directly references ImageSharp **3.2.0**, the vendor-supported v3 security backport, and includes the original, publicly committable assembly-scoped Six Labors Community license `sixlabors.lic`, valid through 2028-01-26. Six package changes were reviewed with an exact-match dependency-manifest gate exception. See [SIXLABORS-LICENSE.md](SIXLABORS-LICENSE.md) and [REVIEWED-IMAGESHARP-DEPENDENCY.md](REVIEWED-IMAGESHARP-DEPENDENCY.md). Do not expand this exception to new packages, new versions, or new assemblies without a separate review.

Six Labors ends security maintenance of the ImageSharp v3 line on **12 May 2027**, per its [published support announcement](https://sixlabors.com/posts/security-support-and-license-enforcement/). Plan an independently tested v4 upgrade well before that date.

## Guarded draft automation

On a new candidate version merged into `main`, the release workflow runs native Windows and Linux packages, tests, checksum checks and 20-second startup smoke tests. It waits for successful C# / Actions CodeQL on the exact commit before creating the *unpublished draft* and its matching version tag. Existing releases or tags are never overwritten. Public publication remains a separate manual decision subject to the acceptance checklist above, including isolated real-install validation and credential-security risk disposition.
