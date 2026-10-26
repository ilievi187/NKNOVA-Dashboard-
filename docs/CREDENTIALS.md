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

## Legacy TripleDES-ECB retirement gate (planned; NOT implemented)

**Security status, 2026-10-26:** GitHub CodeQL review on PR #29 identifies
`Encryption using ECB` at `Nolvus.Services/Lib/LibService.cs:204`
([alert #5](https://github.com/ilievi187/NKNOVA-Dashboard-/security/code-scanning/5)).
Its review thread is unresolved. `DecryptLegacyString` still uses fixed-key
TripleDES/ECB for reading old Nolvus/Mega credentials. New saves use AES-GCM,
but that does **not** remediate the existing legacy read path. Never dismiss
this finding or interpret successful CodeQL analysis jobs as a clean aggregate
CodeQL result. Public release remains blocked.

The following is an **implementation and acceptance plan**, not a claim that
migration, retirement, or end-to-end tests have been completed:

1. **Inventory and consent.** In an isolated test environment, classify each
   Nolvus/Mega/Nexus credential as legacy ECB, legacy plaintext, v2 AES-GCM,
   current v3, missing, or malformed. Never print raw values or keys. Require
   an explicit user action before changing existing stored credentials; a
   successful read alone must not rewrite anything.
2. **Recoverable backup.** Before an owner-approved migration, preserve the
   complete original INI and the *matching* per-user key material using the
   platform's supported DPAPI/profile or Secret Service backup mechanism.
   If a backup cannot be made or verified, do not change the INI or keyring.
   Do not place backups, credentials, or keys in CI artifacts or logs.
3. **Atomic migration.** Decrypt only the selected legacy values in memory,
   re-encrypt with the current user's v3 key, and verify each result by
   decrypting it before committing. Preserve unrelated INI sections, enforce
   existing symlink/path and restrictive-permission checks, flush a sibling
   temporary file, then atomically replace the INI. On any failure, retain
   the original values and allow safe recovery without partial migration.
4. **Mixed-version support.** Prove that an INI containing a mixture of ECB,
   plaintext Nexus, v2 and v3 credentials remains readable before migration,
   and that user-initiated migration changes only intended values. Missing or
   inaccessible DPAPI/Secret Service keys must fail closed; do not generate a
   replacement key when existing encrypted values require the old one.
5. **Acceptance tests.** Run synthetic, isolated Windows DPAPI and Linux
   Secret Service tests for success, cancellation, bad ciphertext, locked
   keyring, wrong user/profile, missing key, interrupted/failed atomic write,
   symlinked target, mixed formats, backup/restore and log redaction. Verify
   the original INI is byte-for-byte unchanged on every failed attempt.
6. **Retire and verify.** Remove the ECB decryption path only after an approved
   compatibility/deprecation decision and proof that supported installations
   can recover or migrate their legacy values. Rerun all required CI and the
   authentic aggregate CodeQL check on the exact PR head; verify the ECB alert
   is resolved by a real code change, not by dismissal or relaxed rules.

The ImageSharp advisories, Six Labors licensing and live installer acceptance
are **independent** release blockers even after the ECB issue is fixed.
