# NKNOVA Rust Install Engine — first native write/download stage

This is a real native **archive ingestion engine** built on top of the
NightKnight Rust UI, not yet a replacement for the full authenticated Nolvus
installer. No installed game, Steam library, MO2 load order, symlinks or
Nolvus instance files are modified by this phase.

## What this implementation actually does

- Parses and validates the NKNOVA JSON manifest, with topological component
  order and validated local source filenames.
- Requires a 64-character SHA-256 pin for **every** user-supplied archive
  before staging. A sample manifest without hashes is intentionally BLOCKED.
- Audits missing files, absent hashes and upstream-managed components.
- Copies only verified authorized archives into the **isolated**
  `<archives>/.nknova-verified/<sha256>.archive` cache.
- Writes to an exclusive `.partial.<pid>.<counter>` file, syncs it and
  publishes with a hard link to avoid overwrite of existing cache objects.
- Re-verifies existing cached objects before reusing them. Corruption BLOCKS
  staging rather than deleting/replacing a potentially user-owned file.
- Appends the verified component ID, hash and size to
  `<archives>/.nknova-verified/receipt-journal.jsonl`.
- Provides real background staging and progress on the UI **The Archive** tab.
- Provides an opt-in Nexus Mods **legacy v1** connector, matching upstream's
  `Nolvus.NexusApi/INexusAPI.cs`: user validation, per-file approved download
  links and HTTPS-only, SHA-256-pinned CDN streaming into the same safe cache.
- Never saves Nexus API keys to configuration or transmits API credentials to
  the CDN. Only a dedicated metadata HTTP client sends the API key.

## App usage — authorized local archive

1. Under Installer enter an absolute **existing** archive directory.
2. Under The Archive enter the absolute path to your JSON manifest, based on
   `install-profiles/nknova-linux.example.json`.
3. Obtain only archives you are entitled to download. Record their **actual**
   SHA-256 hash into each `user-archive` component's `sha256` field.
4. Select **Audit Manifest**. Confirm no unpinned/missing archive entries.
5. Select **Stage Verified Files**. This runs in a background worker and
   commits only checksum-matched immutable copies to the archive cache.

For computing a local hash, use `sha256sum filename.zip` on Linux or
`Get-FileHash filename.zip -Algorithm SHA256` in PowerShell.

## App usage — Nexus API

The Nexus Mods legacy v1 API remains what the original Nolvus dashboard uses.
New Nexus v3 endpoints exist but are NOT assumed compatible with that flow.

Provide a user-generated Nexus Mods personal API key **locally** as an
environment variable before launching the GUI. The key must never be sent in
ChatGPT or committed to GitHub.

- Linux (session-local):
  `read -rsp 'Nexus key: ' NKNOVA_NEXUS_API_KEY; export NKNOVA_NEXUS_API_KEY`
  and launch the app from the same shell.
- Windows PowerShell (session-local):
  `$env:NKNOVA_NEXUS_API_KEY = Read-Host "Nexus API key"`
  then run the executable from that session.

Use **Check Nexus Account** to confirm the API key is accepted.

To download an authorized mod, provide the Skyrim Special Edition Nexus
**mod ID**, **file ID**, and an independently obtained 64-hex-character
**SHA-256**. Premium subscribers may receive links directly if permitted;
free accounts must first obtain an explicit NXM manual download approval
`key` and `expiry` from the actual Nexus mod manager download flow. Enter
them in the transient fields. The software neither invents them nor bypasses
account entitlements.

Use **Download Verified Nexus Archive**. The app gets one of Nexus' approved
CDN URLs, accepts only public HTTPS download/redirect targets and streams
the response to the verified cache with mandatory SHA-256 comparison.
The NXM secret fields are cleared from the UI when the operation begins.

## Limitations — what is not done yet

- No Nolvus package API authenticated modlist metadata importer.
- No automatic Nolvus v6 Graphics Only component graph or remote hash catalog.
- No seamless Nexus login/OAuth or user-friendly NXM protocol handler.
- No resume/range support for interrupted downloads. Incomplete exclusive
  partials are discarded by this implementation; verified objects are reused.
- No cancellation or background install scheduler yet.
- No extraction, overwrite, plugin order generation, ENB binary setup,
  Proton configuration, rollback of external tools or MO2/Fluorine install.
- No claim that a full Skyrim list was installed. The GUI's old compatibility
  launcher stays unchanged for actual Nolvus installs.

## Security notes

- Hard links used for immutable publication require source and destination to
  be on the same volume; temp files live in the target cache directory.
- Symlink parents and symlink archive sources are rejected for local imports.
- Archive names must be safe filenames (no traversal or separators).
- Network download metadata uses a keyed Nexus API client. CDN downloads use
  a **different client** without the API key header. Redirects to HTTP, local
  hostnames, IP hosts and nonstandard ports are not followed.
- HTTPS and hashes do not mean that a file is safe to execute; this phase
  never extracts or runs downloaded binaries.

Source of API semantics: the repository's original
`Nolvus.NexusApi/INexusAPI.cs` and `Nolvus.Package/Files/NexusModFile.cs`.
The user must still comply with Nexus Mods API and download terms.

## Tests

Run `cargo test --workspace` in `rust/`. Tests use synthetic tiny local
archive bytes and never download or touch copyrighted game/mod archives.
Windows/Linux GitHub Actions also build both GUI binaries and capture the
NightKnight UI headlessly on Linux.
