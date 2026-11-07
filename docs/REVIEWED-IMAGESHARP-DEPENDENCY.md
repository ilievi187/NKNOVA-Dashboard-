# Reviewed dependency change — ImageSharp security backport (2026-10-28)

This is an **explicit, narrowly constrained approval of a single dependency migration**, not a generic bypass of the Dependency Manifest Gate. The GitHub Actions gate continues to reject any unreviewed manifest changes.

## Scope

- Package: `SixLabors.ImageSharp` (NuGet), direct reference **3.1.12 to 3.2.0**.
- Exactly six `.csproj` files: `Nolvus.Api.Library.Installer/Nolvus.Api.Installer.csproj`, `Nolvus.Components/Nolvus.Components.csproj`, `Nolvus.Core/Nolvus.Core.csproj`, `Nolvus.Dashboard/Nolvus.Dashboard.csproj`, `Nolvus.Package/Nolvus.Package.csproj`, and `Nolvus.Services/Nolvus.Services.csproj`.
- `Directory.Build.props` may be added solely to route the issued `sixlabors.lic` into build licensing; any PackageReference/PackageVersion/PackageDownload changes there remain blocked.
- Only vendor license ID `ctm_01m60z2tgq281q060a74zxhx0c`, with `Kind=Community`, `ExpiryDateUtc=2028-01-26`, and `Key=Assembly:` scope is accepted for this exceptional migration; the license contents are not reproduced here.
- The PR must **not** modify any security workflow under `.github/workflows/`. The exception does not accept other manifest changes or different versions.

## Security and licensing basis

Six Labors' release notes for [ImageSharp v3.2.0](https://github.com/SixLabors/ImageSharp/releases/tag/v3.2.0) say it backports security patches to the maintained v3 branch. The vendor's [October 9, 2026 security and licensing announcement](https://sixlabors.com/posts/security-support-and-license-enforcement/) confirms required build-time license enforcement for v3.2.0 and official remediation for affected older 3.x versions. The original assembly-scoped community key was issued directly to the maintainer, allows public repository inclusion, and is valid until 2028-01-26.

The initial ImageSharp PR build, C# CodeQL, Linux/Windows solution, package and extracted-app startup smoke tests were successful. Changes to `SixLabors.ImageSharp.Drawing` and `SixLabors.ImageSharp.Web` versions were **not** authorized by this review.

NuGet and GitHub Advisory metadata are independently checked during subsequent release validation. Vendor reports some NuGet records may temporarily lag its security backports; this is **not** permission to disable auditing globally or suppress unrelated security advisories.

## Limitations and release policy

This exception certifies the *specific dependency diff* for the preview build only. It does not certify the inherited credential storage, full Nolvus installation compatibility, or a production/stable release. A publishable preview still needs maintainer acceptance and updated release notes; an isolated end-to-end installation has not yet been demonstrated.

Revoke or tighten the exception after merging the reviewed upgrade, rather than extending it to future package version bumps.
