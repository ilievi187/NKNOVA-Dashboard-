# Six Labors ImageSharp license and upgrade

This repository uses the [Six Labors Split License](https://github.com/SixLabors/ImageSharp/blob/main/LICENSE) for its ImageSharp dependencies, separately from NKNOVA's GPL-3.0 license. The maintainer received a **Community / assembly-scoped** license on 2026-10-28. The license file itself has not yet been included in this branch.

## Licensed assemblies

These names are case-sensitive and come from the current direct `SixLabors.ImageSharp` project references (including explicit `AssemblyName` overrides):

- `Vcc.Nolvus.Api.Installer`
- `Nolvus.Components`
- `Nolvus.Core`
- `NolvusDashboard`
- `Nolvus.Package`
- `Nolvus.Services`

If the contents of the issued `sixlabors.lic` do not cover exactly these assemblies, do **not** bypass validation. Request a corrected community license from Six Labors.

## Use the issued license

Place the **original, unmodified** `sixlabors.lic` file at the repository root (alongside `Directory.Build.props`). The props file explicitly points the Six Labors build task to it when the file exists. Do not invent a license, change its text, or use a key from another project.

Six Labors expressly permits **assembly-scoped community** licenses to be committed in public open-source repositories, as confirmed in its license email and [documentation](https://docs.sixlabors.com/articles/imagesharp.web/). Do not commit unrestricted keys or commercial keys. For such keys use a private `SIXLABORS_LICENSE_KEY` environment variable or secure build secret instead. `Directory.Build.props` respects an explicitly configured license property.

## Change and validation

This candidate deliberately upgrades only the six direct `SixLabors.ImageSharp` references from `3.1.12` to `3.2.0`, the maintained v3 backport for multiple security fixes. It does **not** silently cross to the ImageSharp 4.x API, nor change ImageSharp.Drawing or ImageSharp.Web versions. Package compatibility must be established by full restore, build, tests, NuGet audit, and both platform preview package smoke tests.

Run:

```bash
dotnet restore 'Nolvus Dashboard Linux.sln'
dotnet test 'Nolvus Dashboard Linux.sln' --configuration Release
dotnet build 'Nolvus Dashboard Linux.sln' --configuration Release --no-restore
dotnet list 'Nolvus Dashboard Linux.sln' package --vulnerable --include-transitive
```

NuGet advisories for backported fixes may lag the upstream patch status. Do not blanket-disable NuGet auditing or add generic suppressions. Review each advisory individually against the vendor release and security bulletin, and retain unresolved findings as release blockers.

**Fail-closed release policy:** Until the authentic license is present, all relevant license checks and CI jobs pass, remaining critical advisories are adjudicated, credential migration is reviewed, and an isolated end-to-end install is accepted, NKNOVA remains a preview. Never publish a stable release or modify an existing Skyrim installation as part of this upgrade.
