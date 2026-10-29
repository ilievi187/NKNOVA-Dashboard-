# Troubleshooting

## The application closes immediately

- Confirm that the .NET 10 runtime for your architecture is installed.
- Launch from a terminal and retain the console output. Do not run NKNOVA with elevated/root privileges.
- On Linux, confirm the desktop session and native libraries required by Avalonia/CEF are installed. The CI smoke test uses Xvfb, so it does not prove compatibility with every Wayland compositor or distribution.
- Check the `Logs` directory beside the extracted application. Preserve logs when reporting a problem, but remove or redact account names, access tokens, signed download URLs, and local personal paths before sharing them.

## Credential save fails on Linux

- New credential saves require `secret-tool` (`libsecret-tools`) and a running, unlocked Secret Service-compatible user keyring such as GNOME Keyring or compatible KWallet.
- Unlock the keyring in the same desktop user session that runs NKNOVA, then retry. Do not run NKNOVA with `sudo`; root uses a different Secret Service/keyring.
- NKNOVA fails closed if the keyring is missing, locked, timed out, or cannot be reached. It does not store a new key in a plaintext fallback file, and the account save reports an error without replacing the previous `NolvusDashboard.ini`.
- If the screen reports that a fixed-key legacy password needs re-entry, type the password again and save it to the user vault; NKNOVA intentionally does not decrypt that obsolete format.
- Do not delete `credentials.key`, Secret Service items, or edit `nknova:v2:`, `nknova:v3:`, or `nknova:ref:v4:` values manually. See [Credential storage](CREDENTIALS.md) for legacy compatibility and backup/recovery limits.

## Skyrim is not detected

- Verify that Steam is installed for the same user running NKNOVA.
- Confirm Steam's `steamapps/libraryfolders.vdf` lists the library that contains Skyrim.
- Confirm `steamapps/appmanifest_489830.acf` exists in that library and its `installdir` points to the expected game directory.
- Verify that `SkyrimSE.exe` exists in the game directory. Detection intentionally rejects a manifest-only/incomplete installation.
- Ensure the Steam library is mounted and readable. Do not move or repair game files just to satisfy detection.

## Remote image or thumbnail is not shown

- Remote images must use PNG, JPEG, WebP, GIF, or BMP signatures. TIFF and other formats are rejected, even if the URL ends in an image extension.
- Downloads are limited to 16 MiB and 30 seconds. Identified images above 40 million pixels are rejected, and only the first frame is decoded.
- If an upstream image fails these limits, use an appropriately resized PNG/JPEG/WebP thumbnail. Do not increase limits or use an untrusted image converter without reviewing its resource and security behavior.

## Installation, download, or recovery fails

- Stop before retrying if the destination, required disk space, game edition, Nolvus version, or upstream source is uncertain.
- Keep the existing instance and download cache intact until you know what failed. Do not clear or delete user data as a generic retry step.
- Capture the visible error and relevant sanitized logs. The preview does not claim a complete Nolvus installation or interrupted-install recovery path.
- Verify external tools and Proton configuration through the tool's own supported documentation for your operating system and distribution.

## Updating or rolling back NKNOVA

The inherited upstream auto-updater is disabled. Obtain future packages from a verified NKNOVA release, verify its SHA-256 checksum, and install it into a separate user-writable directory first. Keep the prior package until the new one starts correctly. If it fails, close it and remove only the new test directory. Do not remove Skyrim, Nolvus instances, Steam libraries, Proton prefixes, or shared caches.

## Report a reproducible bug

Include the operating system/version, CPU architecture, NKNOVA commit or package version, exact safe reproduction steps, and sanitized logs. Do not attach game files, mod archives, credentials, Nexus API keys, signed URLs, or private profile data.
