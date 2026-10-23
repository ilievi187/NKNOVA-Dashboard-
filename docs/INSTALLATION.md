# Installation and safe testing

## Preview scope

NKNOVA `0.1.0-preview.1` is an unpublished candidate, not a stable release. CI can build Linux x64 and Windows x64 packages, validate archives, run automated tests, and confirm that an extracted application process stays open during a brief startup smoke test. It does not prove that NKNOVA can complete a real Nolvus installation.

Do not use your only game/modlist installation for initial testing. Make a backup and, where possible, test with a separate game profile and a separate writable NKNOVA directory. No game files are distributed with NKNOVA.

## Linux x64

1. Install a supported .NET 10 x64 runtime and a desktop session with the libraries required by Avalonia/CEF.
2. Install Steam and ensure the desired Skyrim Special Edition library is mounted and accessible to your user.
3. Extract `NKNOVA-Linux-0.1.0-preview.1.tar.gz` into a writable directory owned by your user.
4. From the extracted directory, start `./Nolvus/NolvusDashboard`.
5. Check the Dashboard's diagnostics and detected game path. Steam discovery needs a readable `steamapps/libraryfolders.vdf`, the Skyrim Special Edition app manifest (`489830`), and the installed `SkyrimSE.exe`.
6. Do not run NKNOVA with `sudo`. Use the normal desktop user so Steam, Proton, cache, and configuration paths resolve correctly.

The inherited Linux workflow can depend on Steam/Proton tools such as `protontricks` and `winetricks`; their availability and behavior vary by distribution. Install and update these using your distribution's supported package-management method. Do not paste commands that run the application as root.

## Windows x64

1. Install a supported .NET 10 x64 runtime.
2. Extract `NKNOVA-Windows-0.1.0-preview.1.zip` into a writable directory under your user profile.
3. Start `NolvusDashboard.exe`.
4. Confirm the detected Steam library and Skyrim Special Edition directory before choosing an install or repair action.

Steam discovery checks common Windows Steam locations, the current-user Steam registry path, and configured libraries. The presence of `SkyrimSE.exe` is required. This does not validate the full Windows Nolvus installation workflow.

## Build from source

Requires the .NET 10 SDK.

Linux:

```bash
dotnet test "Nolvus Dashboard Linux.sln" --configuration Release
./build.sh
```

The Linux script builds the Dashboard-only archive at `~/Desktop/Binaries-<version>.tar.gz`, removes debug symbols, excludes the upstream updater, and refuses to overwrite an existing archive.

Windows PowerShell:

```powershell
dotnet test 'Nolvus Dashboard Linux.sln' --configuration Release
.\scripts\package-windows.ps1 -OutputDirectory .\artifacts
```

The Windows script publishes the dashboard, checks archive paths and prohibited content, extracts the package, and performs a short startup smoke test with an isolated user profile. It refuses to overwrite existing archive/checksum files.

## Rollback

Before changing or replacing an existing NKNOVA build, retain the old package and back up the NKNOVA configuration and any instance-specific settings. If a preview test fails, close NKNOVA and remove only the new extracted NKNOVA directory created for that test. Do not delete or overwrite a Steam library, Skyrim folder, Nolvus instance, Proton prefix, or shared download cache as a generic recovery step.
