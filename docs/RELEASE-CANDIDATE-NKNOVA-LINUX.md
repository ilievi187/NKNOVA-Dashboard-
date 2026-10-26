# NKNOVA Linux Installer v0.1 — release candidate notes (DRAFT)

**Status: NOT APPROVED FOR PUBLIC RELEASE.** This candidate is a read-only developer preflight, **not** a working one-click Nolvus installer. Do not label it stable or advertise it as a completed Nolvus installation.

## Scope

- .NET 9 CLI with schema-v1 JSON manifest, dependency ordering and strict input validation.
- Read-only discovery of Steam Skyrim, GE-Proton, Fluorine and XWayland.
- Steam `installdir` path-traversal rejection.
- Mandatory SHA-256 verification for user-provided archives before READY: missing digest, mismatch, unreadable files, and unavailable dependencies block readiness. The example manifest deliberately omits real archive hashes and remains BLOCKED until authorized files are pinned.
- No Steam, Skyrim, Proton, saves, MO2 or game-file writes. No automatic downloads or game launch.

## Developer smoke test

From a clean checkout of the candidate commit with .NET 9 SDK:

```sh
dotnet build NKNOVA.Installer.Cli/NKNOVA.Installer.Cli.csproj -c Release
dotnet run --project NKNOVA.Installer.Tests/NKNOVA.Installer.Tests.csproj -c Release
dotnet run --project NKNOVA.Installer.Cli/NKNOVA.Installer.Cli.csproj -c Release -- plan --manifest install-profiles/nknova-linux.example.json --archives /path/to/authorized-archives
```

Expected for the sample profile: the CLI reports BLOCKED for the unimplemented upstream adapter and returns exit code 1. This is correct, not an installation failure. Invalid invocation returns 2.

## Release gate — all items required before a user-facing NKNOVA installer release

- [x] Installer Core, main CI, security gates, resolver checks passed on commit `fc394e68fbe9787e883be34ecdb6f4077b15c450` (historical checkpoint; rerun on final release SHA).
- [x] Read-only preflight, SHA-256 and dependency regression tests implemented.
- [ ] Merge and retest Linux installer PR #1 against latest `main`; it remains a draft.
- [ ] Rebase and resolve conflicts in dependent graphics-only PR #2, then retest its Windows/Linux artifacts.
- [ ] Resolve blocking CodeQL legacy TripleDES-ECB finding in credentials PR #29 with a safe migration strategy; do not waive the finding or lose existing user secrets.
- [ ] Implement and test upstream Nolvus adapter, verified download/install journal, atomic staging, rollback and no-overwrite protection.
- [ ] Run real Linux Steam/Proton/Fluorine smoke test on an authorized disposable environment; no modifications to a working Nolvus instance.
- [ ] Produce checksummed, reproducible artifacts for supported OSes; verify installation/launch and license/third-party notices.
- [ ] Review security and sign off release notes; only then publish an appropriate versioned release.

## Known limitations

This branch does not perform installation. The upstream adapter is deliberately BLOCKED. No real-game installation or graphics-only path is verified. Archive names in the sample manifest are placeholders; do not redistribute paid mods, third-party archives or Skyrim game files. Preserve GPL-3.0 license and original project credits.

## Proposed versioning

Use a distinct **developer preview** identifier (for example `v0.1.0-preflight.1`) only if a pre-release is intentionally authorized; do not use a stable `v1.0` tag for the preflight. A full NKNOVA one-click installer needs its own later release after the above gates pass.
