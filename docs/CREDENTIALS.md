# Credential storage

New account credentials are stored in the operating system's user credential vault. `NolvusDashboard.ini` contains only random `nknova:ref:v4:` references, never the credential itself or an encrypted copy of the credential.

- **Windows:** Credential Manager stores each secret under the current user. Before storage, NKNOVA protects the UTF-8 secret with current-user DPAPI using `CRYPTPROTECT_UI_FORBIDDEN` and without the machine-wide flag.
- **Linux:** the current user's freedesktop Secret Service stores the secret through `secret-tool`/libsecret, commonly provided by GNOME Keyring or a compatible KWallet service. Secret data is sent through stdin, not process arguments or logs. If the service is absent, locked, or times out, saving fails closed. There is no plaintext-file fallback.

The random reference in the INI is not a password or key. Retrieving a reference requires the same user's credential vault and matching vault item. The app moves vault access off the UI thread and bounds Secret Service subprocess waits.

## Legacy compatibility and re-entry

- v2/v3 AES-GCM envelopes from earlier builds are read-only compatible. Their matching key may remain in the prior per-user key file or Secret Service item; NKNOVA does not rewrite or remove those keys automatically.
- The former fixed-key TripleDES/ECB Nolvus/Mega format is no longer decrypted. The UI asks the user to re-enter that password and save it to the OS vault. This avoids continuing use of a weak, shared, unauthenticated cipher.
- Legacy plaintext Nexus API keys remain readable so users can complete the normal account flow; a successful explicit account save replaces the INI value with a v4 reference.
- Reading any legacy value does not migrate it. Unsaved legacy passwords remain at their old risk until the user re-enters them.

When a credential is saved, NKNOVA creates a new OS-vault item and atomically replaces the INI only after the complete new configuration is flushed. If the INI update fails, it attempts to remove only the new item(s) created by that failed save. Old references are not deleted automatically after a successful replacement; this favors recoverability over silent deletion and may leave unused vault items.

## Backup and downgrade

Protect `NolvusDashboard.ini` together with the user's OS credential store. On Windows, preserve the matching current-user DPAPI profile and Credential Manager data. On Linux, use the desktop keyring's supported backup/migration mechanism. Copying only the INI does not transfer the credential items. Do not delete or recreate vault items when a v4 reference cannot be retrieved. Restore the matching user profile/keyring or re-enter credentials through the supported UI.

Older NKNOVA builds do not understand `nknova:ref:v4:` references. Before downgrading, keep the current application and user credential store intact. Never edit reference values, vault entries, or legacy key files by hand.

## Linux prerequisites and troubleshooting

Credential saving requires:

1. `secret-tool` from `libsecret-tools`.
2. A running Secret Service implementation on the user's D-Bus session, such as GNOME Keyring or compatible KWallet.
3. An unlocked user keyring.

On Ubuntu/Debian, install the desktop keyring provider and `libsecret-tools` using the distribution package manager. Do not run NKNOVA with `sudo`, because root uses a different user keyring.

If NKNOVA reports that Secret Service is unavailable, unlock the user's desktop keyring and retry. The account-save UI leaves the previous INI unchanged. Do not copy or delete `credentials.key` or Secret Service data as a generic repair step.

## Test boundaries

The Linux regression harness creates unique synthetic Secret Service item scopes, tests v4 save/read across service instances, and removes only those synthetic items. It also tests legacy AES envelope behavior without real credentials. Windows CI exercises the Credential Manager/DPAPI path with temporary synthetic credentials. These tests do not read real user credentials, change a game installation, or prove a full Nolvus installation.
