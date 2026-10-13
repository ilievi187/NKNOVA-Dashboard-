# NKNOVA NightKnight — installer UX implementation plan

**Status:** Implemented in this branch as a five-step native Rust configurator.
The actual Nolvus install orchestrator remains a separate backend milestone.

## Workflow (committed implementation)

1. **Edition:** Four mutually exclusive official Nolvus Awakening v6
   variants: **Graphics Only**, **Redux**, **Ultra**, **Ultimate**. Display
   description and official approximate storage / archive requirements.
   Optional **SR Exterior Cities** toggle changes the requested future plan,
   not installer behavior yet. Do not combine Graphics Only with Redux/Ultra.
   These controls work without Skyrim installed.
2. **Visuals:** ENB selector, description, four user-owned per-scene screenshot
   slots and direct links to the verified corresponding **author gallery**
   (not a generic fantasy stock photo). Artwork cards are labeled stylized.
   Local approved captures for *day, forest, night, interior* can render at
   native pixel quality when present.
3. **Paths:** Choose game path, new empty install target and independent
   archive cache. Skyrim discovery is optional until final preflight.
4. **Resources:** Show real Nexus single-transfer MiB/s throttle
   (0 = unlimited); store capped concurrency (1..16), extraction RAM budget,
   CPU worker budget, retries and background preferences for the still-to-be-
   built scheduler. Label these non-enforced controls clearly.
5. **Review:** Show an explicit saved *configuration*, distinguish the
   read-only Graphics Only preflight from unavailable native installation
   for other editions. Don't silently transfer selection into the legacy
   Nolvus Dashboard; user reconfirms there.

## ENB image provenance and slots

Bundling arbitrary Nexus author images would require the authors' permission.
This branch **does not redistribute** their images; it links directly to
verified original gallery pages:

| ENB | Original creator | Author screenshot gallery |
|---|---|---|
| Cabbage | Cabbage / aaa3561080 | https://www.nexusmods.com/skyrimspecialedition/mods/103042?tab=images |
| Kauz | Kynias-Kauz | https://www.nexusmods.com/skyrimspecialedition/mods/111431?tab=images |
| PI-CHO | HaeVakSa | https://www.nexusmods.com/skyrimspecialedition/mods/35082?tab=images |
| Amon Reborn | zxamonxz | https://www.nexusmods.com/skyrimspecialedition/mods/99786?tab=images |

The user may place self-captured or authorized PNG/JPEG images in
`<nknova-user-config>/enb-previews/<enb>/<scene>.png`
(or `.jpg` / `.jpeg`). For example:

`~/.config/nknova/enb-previews/cabbage/day.png`

`%APPDATA%\nknova\enb-previews\kauz\night.jpg`

Valid ENB names: `cabbage`, `kauz`, `picho`, `amon`.
Valid scenes: `day`, `forest`, `night`, `interior`.

The app displays only local regular files, rejects symlink paths, and limits
image size to 8 MiB and dimensions to 4096 x 2160.

**Comparability caveat:** The original author galleries show authentic
captures, but NOT guaranteed same location, time of day, weather or game
version. Do not claim a controlled A/B test without matched captures.
Avoid fake FPS/performance badges; requirements depend on variant, resolution
and hardware.

## Backend state / future work

- The native Nexus CDN adapter now enforces the configured transfer speed
  per download. Download transfers remain one at a time in current GUI.
- Background priority, concurrency, RAM, CPU and retry settings are persisted,
  validated and displayed but **not yet enforced**.
- Actual install scheduling, auto-pause/resume, archive extraction, ENB
  composer, Nolvus API package resolution, cancellation and rollback are
  *not* completed by this UI branch.
- Existing Skyrim files, saves, symlinks, modlists and C# branch remain
  unchanged. No deployment from this branch without user initiation.

## Acceptance checks

- Rust `cargo test --workspace` on Windows and Ubuntu Actions.
- Rust GUI and CLI release build on Windows and Ubuntu.
- Linux headless startup screenshots of edition, ENB preview, resource
  controls, and archive page.
- Confirm selected variant persists across settings JSON save/load.
- Assert unsupported Redux/Ultra/Ultimate installs are blocked rather
  than accidentally launched as Graphics Only.
- Never mark future resource budgets or fake install progress as active.

## Official source

Nolvus Awakening v6: https://www.nolvus.net/appendix/installer/requirements
and https://www.nolvus.net/appendix/installer/install
