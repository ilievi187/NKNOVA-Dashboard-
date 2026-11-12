# NKNOVA Dashboard — Linux installer v0.1

This is a GPL-3.0 fork of sableeyed/NolvusDashboard. Preserve original author credits and license.

## Implemented now

- .NET 9 core library, versioned JSON manifest and deterministic dependency ordering.
- Reject path traversal, duplicate components, unknown/circular dependencies, unsupported
  schema, and malformed SHA256 pins.
- Read-only discovery of Steam Skyrim installation, pinned GE-Proton, Fluorine and XWayland.
- Command-line preflight plan. This NEVER writes game files or runs the game.
- User-provided archives are READY only when present **and** their bytes match a pinned
  SHA-256 digest. An archive without a digest stays BLOCKED even when the file exists.
  The example manifest intentionally omits real archive hashes until authorized
  artifacts are supplied; never invent hashes to make a preflight pass.
- Basic standalone tests and GitHub Actions compilation.

## Not implemented yet

The installer itself, automatic Proton download, authenticated mod acquisition, automatic
Community Shaders selection, MO2 profile assembly, Nemesis output, rollback and GUI are not
part of v0.1. Missing items are explicitly BLOCKED rather than pretending to be installed.

Sample manifest archive filenames are placeholders supplied by the user, not real upstream
download links. Never commit third-party Skyrim binaries, paid mod archives or protected
game data. Always obtain them through lawful authorized channels.

## Quick start

With .NET 9 SDK, from the repository root:

    dotnet run --project NKNOVA.Installer.Cli -- plan --manifest install-profiles/nknova-linux.example.json --archives /path/to/authorized-archives

Return codes: 0 = all preflight prerequisites present, 1 = blocked, 2 = invalid invocation.
A blocked upstream installation adapter is expected at this stage.

## Delivery design

- C#/.NET 9 + existing Avalonia Dashboard, not a competing rewrite.
- Keep upstream Nolvus installer and API; compose NKNOVA add-ons after upstream install.
- Graphics profiles must be mutually exclusive: Community Shaders versus ENB.
- Downloads: explicit provenance, Nexus entitlement, pinned version and checksum,
  content-addressed cache, atomic staging, operation journal and resumability.
- Proton and DXVK: detect and install pinned official releases, use verified game version,
  create isolated and reproducible compatdata only after user confirms.
- MO2/Fluorine: manage any symlink, Wine drive and mount internally; never ask users
  to edit them. Dedicated modlist and plugin order per profile; never share mutable
  generated output between unrelated profiles.
- Do not delete or change a working Nolvus installation, symlinks, saves or backups.
- No automatic game launches, benchmarks or GUI control without explicit authorization.
- Add fault-injection and rollback tests before enabling write operations.

Next: connect to upstream Nolvus.Package installer, add an install journal + dry-run
download manifest, then profile composer and GUI. Current stable NightKnightPC install
is outside the project's working directory and remains untouched.
