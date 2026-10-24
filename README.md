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
- ImageSharp 3.1.12 remains in six direct-reference projects and generates five distinct NuGet advisories (three high, two moderate). The current September 2026 advisory set identifies 4.1.2 or later as patched; 3.2.0 does not resolve the full set. The preserved v4.1.3 stash is a dependency-only candidate, not validated or applied. A build-time Six Labors license is required for the upgrade, and no application/key has been approved or added. See [Release process](docs/RELEASING.md).
- New credential saves use AES-GCM, with the key stored via Windows current-user DPAPI or Linux Secret Service. Existing TripleDES Nolvus/Mega values and plaintext Nexus keys remain readable for compatibility; they are not silently rewritten and remain at their prior risk until the user explicitly saves credentials again. Linux credential saving requires an unlocked Secret Service-compatible keyring. See [Credential storage](docs/CREDENTIALS.md).

## Downloads

The current candidate is `v0.1.0-preview.2`, which supersedes the unpublished `v0.1.0-preview.1` draft. The release workflow builds and verifies Linux/Windows assets and creates an **unpublished GitHub draft pre-release**. It must not be published publicly while the ImageSharp advisory/license gate and full real-install validation remain unresolved. Check [GitHub Releases](https://github.com/ilievi187/NKNOVA-Dashboard-/releases) for release status.

Release assets are x64 framework-dependent packages, with SHA-256 files. Install the .NET 10 runtime before launching. If a runtime is missing, install the .NET 10 Desktop Runtime on Windows or the .NET 10 runtime package for your Linux distribution. Keep the extracted folder writable because the current application writes local settings, logs, and cache data relative to its application directory.

### Linux

1. Install a current .NET 10 x64 runtime, Steam, and the external tools required by the particular Proton workflow you intend to use. Credential saving also requires a running/unlocked Secret Service-compatible user keyring (for example GNOME Keyring or a compatible KWallet service).
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
dotnet test Nolvus.Core.Tests/Nolvus.Core.Tests.csproj --configuration Release
bash scripts/run-services-regressions-linux.sh
./build.sh
```

The focused Linux services harness uses an isolated D-Bus/GNOME Keyring test home. It requires `dbus-run-session`, `gnome-keyring-daemon`, and `secret-tool` (`libsecret-tools`). `build.sh` publishes the dashboard only, omits the upstream updater, and refuses to overwrite an existing `~/Desktop/Binaries-<version>.tar.gz` archive.

On Windows PowerShell:

```powershell
dotnet test 'Nolvus Dashboard Linux.sln' --configuration Release
dotnet test Nolvus.Core.Tests/Nolvus.Core.Tests.csproj --configuration Release
dotnet run --project Nolvus.Services.Tests/Nolvus.Services.Tests.csproj --configuration Release
.\scripts\package-windows.ps1 -OutputDirectory .\artifacts
```

The Windows package script validates archive paths and contents, extracts the generated zip, and runs a short startup smoke test with an isolated profile. The Linux and Windows package scripts do not install Nolvus or modify a user's Steam/game data as part of packaging.

## Troubleshooting and recovery

See [Installation](docs/INSTALLATION.md), [Troubleshooting](docs/TROUBLESHOOTING.md), [Credential storage](docs/CREDENTIALS.md), and [Release process](docs/RELEASING.md). Preserve your original package and configuration backups until the new build is known to start. To roll back a failed preview test, exit NKNOVA and remove only the new extracted NKNOVA directory that you created. Do not delete the Steam library, game directory, Nolvus instance, or shared download cache as a generic troubleshooting step.

## Licensing and attribution

NKNOVA source code is distributed under GPL-3.0. See [LICENSE](LICENSE) and retain upstream attribution. Nolvus, Skyrim, Steam, Proton, Nexus Mods, Avalonia, and Six Labors products are third-party names and software governed by their respective terms. No game assets are redistributed by NKNOVA.

ImageSharp 3.1.12 remains in use while maintainers evaluate the existing v4.1.3 candidate. Five advisories affect this version; the Sep 15, 2026 advisory set lists 4.1.2 as patched, so v3.2.0 is not sufficient for the full set. Direct references to ImageSharp 3.2.0 and later require a valid Six Labors license at build time. No community-license application has been submitted and no key is present. An authorized maintainer must apply with the exact assemblies and store any approved key as a private GitHub Actions secret, never in tracked files. See the official [ImageSharp license/build docs](https://docs.sixlabors.com/articles/imagesharp/) and [v4.1.3 release notes](https://github.com/SixLabors/ImageSharp/releases/tag/v4.1.3).

## Credits

- The Nolvus team and the upstream Nolvus Dashboard contributors.
- The original Linux Dashboard maintainers and contributors.
- The Avalonia, .NET, Proton, Steam, and Linux gaming communities.
- Everyone contributing code, tests, documentation, and issue reports.
