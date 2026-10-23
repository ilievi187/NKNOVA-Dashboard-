![Nolvus Banner](Nolvus.Dashboard/Assets/background-nolvus-banner.svg)

# NKNOVA Launcher

NKNOVA is a GPL-3.0-licensed community fork of the Avalonia-based Nolvus Dashboard. It currently packages the existing dashboard application for Linux x64 and Windows x64. This is a **preview**, not a stable release and not a claim that every Nolvus installation path works end to end.

> **Preview safety notice:** A complete live Nolvus installation has not been validated for this release. Back up existing game and modlist data before testing, use a separate test instance, and do not point recovery or install actions at your only working setup. No Skyrim or Nolvus game data is included.

## Current status

- The Avalonia dashboard and inherited Nolvus installer/package workflows remain in the application.
- Linux and Windows package jobs validate the package contents and perform a short extracted-app startup smoke test.
- Steam discovery checks the Skyrim Special Edition app manifest and the presence of `SkyrimSE.exe`; Linux Steam and Windows Steam discovery use platform-specific roots.
- File-copy path checks, URL-log redaction, malformed-rule behavior, and selected service behavior have automated regression coverage.
- Remote thumbnail downloads are capped at 16 MiB and 30 seconds. The preview accepts PNG, JPEG, WebP, GIF, and BMP, caps decoded images at 40 million pixels, and decodes only the first frame. Unsupported TIFF/other formats and oversized files are rejected to bound memory use.
- The inherited upstream updater is disabled and not packaged. Install later NKNOVA versions manually from a verified release.
- The `LibService` API remains synchronous. Current UI callers move network decoding into background tasks with bounded concurrency; future callers must not invoke it directly on the UI thread.

## Not verified or not included

- A full download, extraction, and installation of a Nolvus list was not performed.
- Windows Nolvus installation, Proton-prefix workflows, and Windows recovery have not been demonstrated end to end.
- Redux, Ultimate, Ultra, Graphics-only, ENB, and Community Shaders availability depends on the actual upstream service/API and installed configuration. The preview does not promise all variants are installable.
- No new, standalone Rust installer or custom one-click installation backend is included.
- RAM/CPU/download resource-limit controls and interruption recovery are not claimed unless the current UI/backend explicitly enforces them.
- ImageSharp 3.1.12 remains in the dependency graph pending a vendor-approved upgrade and license. Current advisories have patched v3/v4 lines, but build-time license approval is needed to update. The public preview remains blocked until this gate is resolved.
- Existing Nolvus/Mega account settings use inherited reversible client-side obfuscation with a built-in key, not secure credential storage. No user credential is bundled, but testers should not rely on it to protect passwords from local access. A backward-compatible OS/user-scoped storage migration is required before a stable release.

## Downloads

The current candidate is `v0.1.0-preview.2`, which supersedes the unpublished `v0.1.0-preview.1` draft. The release workflow builds and verifies Linux/Windows assets and creates an **unpublished GitHub draft pre-release**. It must not be published publicly while the ImageSharp advisory/license gate and full real-install validation remain unresolved. Check [GitHub Releases](https://github.com/ilievi187/NKNOVA-Dashboard-/releases) for release status.

Release assets are x64 framework-dependent packages, with SHA-256 files. Install the .NET 10 runtime before launching. If a runtime is missing, install the .NET 10 Desktop Runtime on Windows or the .NET 10 runtime package for your Linux distribution. Keep the extracted folder writable because the current application writes local settings, logs, and cache data relative to its application directory.

### Linux

1. Install a current .NET 10 x64 runtime, Steam, and the external tools required by the particular Proton workflow you intend to use.
2. Extract `NKNOVA-Linux-0.1.0-preview.2.tar.gz` to a user-writable directory, not `/usr` or another protected location.
3. Run `./Nolvus/NolvusDashboard` from the extracted package.
4. Review diagnostics and verify the detected game path before starting any installation or repair action.

Do not run the application with `sudo`. Keep your existing Steam library and modlist unchanged during initial testing.

### Windows

1. Install a current .NET 10 x64 runtime.
2. Extract `NKNOVA-Windows-0.1.0-preview.2.zip` to a user-writable directory.
3. Run `NolvusDashboard.exe`.
4. Confirm Skyrim Special Edition is detected correctly before using installation or repair actions.

### Build from source

Install the .NET 10 SDK. On Linux, from the repository root:

```bash
dotnet test "Nolvus Dashboard Linux.sln" --configuration Release
./build.sh
```

`build.sh` publishes the dashboard only, omits the upstream updater, and refuses to overwrite an existing `~/Desktop/Binaries-<version>.tar.gz` archive.

On Windows PowerShell:

```powershell
dotnet test 'Nolvus Dashboard Linux.sln' --configuration Release
.\scripts\package-windows.ps1 -OutputDirectory .\artifacts
```

The Windows package script validates archive paths and contents, extracts the generated zip, and runs a short startup smoke test with an isolated profile. The Linux and Windows package scripts do not install Nolvus or modify a user's Steam/game data as part of packaging.

## Troubleshooting and recovery

See [Installation](docs/INSTALLATION.md), [Troubleshooting](docs/TROUBLESHOOTING.md), and [Release process](docs/RELEASING.md). Preserve your original package and configuration backups until the new build is known to start. To roll back a failed preview test, exit NKNOVA and remove only the new extracted NKNOVA directory that you created. Do not delete the Steam library, game directory, Nolvus instance, or shared download cache as a generic troubleshooting step.

## Licensing and attribution

NKNOVA source code is distributed under GPL-3.0. See [LICENSE](LICENSE) and retain upstream attribution. Nolvus, Skyrim, Steam, Proton, Nexus Mods, Avalonia, and Six Labors products are third-party names and software governed by their respective terms. No game assets are redistributed by NKNOVA.

ImageSharp 3.1.12 remains in use while maintainers evaluate an upgrade. The current Six Labors advisories identify ImageSharp 3.2.0 as patched for the affected v3 lines and 4.1.2 or later for the affected v4 lines. Direct references to ImageSharp 3.2.0 and later require a valid Six Labors license at build time. Any community license is application-based; no key is embedded in this repository or release. See the official [Six Labors licensing page](https://sixlabors.com/pricing/) and [ImageSharp license documentation](https://docs.sixlabors.com/articles/imagesharp/) before changing the package version.

## Credits

- The Nolvus team and the upstream Nolvus Dashboard contributors.
- The original Linux Dashboard maintainers and contributors.
- The Avalonia, .NET, Proton, Steam, and Linux gaming communities.
- Everyone contributing code, tests, documentation, and issue reports.
