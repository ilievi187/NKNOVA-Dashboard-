# Credential storage

NKNOVA writes newly saved credentials as authenticated AES-GCM envelopes in `NolvusDashboard.ini`. The AES key is stored separately within the current user's security boundary:

- **Windows:** a random 256-bit key is DPAPI-protected for the current user and stored under `%LOCALAPPDATA%\NKNOVA\Credentials\credentials.key`. NKNOVA does not request machine-wide DPAPI protection.
- **Linux:** a random 256-bit key is stored by the current user's freedesktop Secret Service (through `secret-tool`/libsecret), commonly provided by GNOME Keyring or a compatible KWallet service. The key is not written to a plaintext key file. NKNOVA fails closed if the Secret Service utility or unlocked user keyring is unavailable.

The credential values use the `nknova:v3:` envelope. The Linux Secret Service item is scoped to the NKNOVA credential key and version. The v2 AES-GCM file-key envelope remains read-only for compatibility. The earlier fixed-key TripleDES Nolvus/Mega format is also read-only. Legacy plaintext Nexus API keys remain readable.

## Migration behavior

NKNOVA never rewrites credentials merely because it reads them. After a user explicitly completes the account flow or saves Mega credentials, the newly saved values use the current v3 protection. Existing Nolvus/Mega or plaintext Nexus values remain in their old format until the user explicitly saves them again. Do not describe those legacy values as protected.

Saving credentials is transactional with respect to `NolvusDashboard.ini`: NKNOVA writes a restrictive sibling temporary file and replaces the INI only after the new contents are flushed. If secure key storage or file persistence fails, the UI reports a generic recovery action and leaves the previous INI unchanged. No credential value, key, or Secret Service command output is logged.

## Backup and downgrade

Protect `NolvusDashboard.ini` and the associated credential key together. On Windows, preserve the matching current-user DPAPI profile. On Linux, use the desktop keyring's supported backup/migration mechanism. Copying only the INI does not transfer the key. Do not delete or recreate a keyring item when a v3 value cannot be decrypted. Restore the matching key from the same user profile or use the platform's supported recovery procedure.

Older NKNOVA builds do not understand `nknova:v3:` values. Before downgrading, keep the current application and its user security store intact; re-enter credentials only through the supported application UI after restoring a compatible build. Never edit encrypted values or key files by hand.

## Linux prerequisites and troubleshooting

Credential saving requires:

1. `secret-tool` from `libsecret-tools`.
2. A running Secret Service implementation on the user's D-Bus session, such as GNOME Keyring or a compatible KWallet service.
3. An unlocked user keyring.

On Ubuntu/Debian, install the desktop's keyring provider and `libsecret-tools` using the distribution package manager. Do not run NKNOVA with `sudo`, because the root account would use a different user keyring.

If NKNOVA reports that Secret Service is unavailable, unlock the user's desktop keyring and retry. Existing settings are not automatically migrated or deleted. Keep a backup of the original INI and keyring before account changes.

## Test boundaries

The Linux regression harness creates a unique synthetic keyring scope, tests save/read across service instances, then clears only that synthetic item. Windows CI tests the DPAPI code path using temporary test data. These tests do not read real user credentials, change a game installation, or prove that a full Nolvus installation works.
