# NKNOVA quick install: Nolvus v6 Graphics Only + ENB (alpha)

This feature lives **inside the existing Avalonia Nolvus Dashboard** and works from
the existing instance selection screen. It does not create a competing installer or
replace upstream download/install logic.

## What does "barebones" mean here?

The upstream **Graphics Only** variant of **Nolvus Awakening v6** supplies the
graphics overhaul, ENB and required dependencies, not the full gameplay overhaul.
This is still a *large* Nolvus modlist, not a vanilla Skyrim/ENB-only installer:
budget roughly **235 GB installed**, plus around **105 GB in downloads** if
archives are retained; actual requirements can change upstream. Do not install
on an HDD.

## Using it on Windows or Linux

1. Install and run Steam Skyrim SE with the Anniversary Edition content.
2. Run the corresponding NKNOVA Dashboard build; configure your owned Skyrim
   directory, Nexus authorization and Nolvus login using the existing settings flow.
3. On the instance selection page, pick **Cabbage**, **Kauz**, **PI-CHO**, or
   **Amon Reborn** from the ENB preset control.
4. Click **Install Graphics Only + ENB**. The app verifies the upstream v6
   Graphics Only variant, checks that the game is present, refuses to overwrite
   a registered/non-empty instance, configures a safe 1080p fallback or the
   primary display resolution, and enters the stock-game/mod installation
   pipeline with the selected ENB already configured.

If upstream flags v6 as beta, you will see its beta disclaimer before install.
Nexus **non-Premium** accounts will still need to confirm third-party downloads;
we cannot legally or technically bypass those download flows.

Default install and archive paths are supplied by the existing dashboard:
the new instance is placed under its `Instances` directory and downloaded
archives are reused via its `ARCHIVE` folder. Make sure the dashboard itself
lives on an SSD in a writable short path (on Windows, e.g. `C:\Nolvus`).

**No reinstall/repair/overwrite mode is implemented for the quick button.**
For existing installations, continue to use upstream resume/repair/manage
functions. The button deliberately blocks instead of modifying an occupied path.

## Developer verification

From the repository root with the .NET 10 SDK:

```sh
dotnet test Nolvus.Core.Tests/Nolvus.Core.Tests.csproj
dotnet build Nolvus.Dashboard/Nolvus.Dashboard.csproj -c Release
```

The workflow `NKNOVA CI` builds and validates on Windows and Linux.
Fork builds deliberately skip the upstream Linux auto-updater to prevent it
from replacing NKNOVA. Download newer fork builds from this repository.

Neither source compilation nor simulated tests are a live Skyrim/Proton
integration test. **Do not treat the quick installer as production-proven**
until it completes an authorized clean install on each OS.

## Shader mutual exclusivity

- The **Community Shaders** profile and **ENB** presets are strictly mutually
  exclusive (`CompatibilityRules.ValidateShaderMutualExclusivity`).
- Selecting an ENB preset automatically prevents Community Shaders from hooking
  into the DirectX 11 pipeline, and vice versa.

Keep Skyrim, existing MO2/Fluorine instances, saves and backups untouched.
