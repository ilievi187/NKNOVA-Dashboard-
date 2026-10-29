using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Nolvus.Core.Interfaces;
using Nolvus.Core.Services;
using Nolvus.Services.Files;
using Nolvus.Services.Game;
using Nolvus.Services.Globals;
using Nolvus.Services.Lib;
using Nolvus.Services.Settings;

internal static class Program
{
    private static int Main()
    {
        var signedUrl =
            "https://user:secret-password@downloads.example.test/private/mod.zip" +
            "?api_key=SECRET_API_KEY&signature=SECRET_SIGNATURE&expires=999999999#fragment";

        var signedUrlLogs = CaptureDownloadUrlLog(signedUrl);
        var secrets = new[]
        {
            "secret-password",
            "SECRET_API_KEY",
            "SECRET_SIGNATURE",
            "/private/mod.zip",
            "api_key",
            "signature",
            "expires",
            "fragment",
            "999999999",
            "user:"
        };

        foreach (var secret in secrets)
        {
            if (signedUrlLogs.Contains(secret, StringComparison.Ordinal))
                throw new Exception("FAILED: signed URL secret reached logs: " + secret);
        }

        if (!signedUrlLogs.Contains("downloads.example.test", StringComparison.Ordinal))
            throw new Exception("FAILED: safe host missing from logs");

        var malformedUrl = "not a valid URI?signature=UNPARSEABLE_SECRET";
        var malformedUrlLogs = CaptureDownloadUrlLog(malformedUrl);
        if (!string.Equals(malformedUrlLogs.Trim(), "URL: [redacted]", StringComparison.Ordinal))
            throw new Exception("FAILED: malformed URL was not fully redacted");

        RunSteamLibraryDetectionRegression();
        RunRemoteImageLoaderRegression();
        RunCredentialProtectionRegression();
        RunCredentialSettingsIntegrationRegression();
        if (OperatingSystem.IsLinux())
        {
            RunLinuxLegacyV3SecretServiceRegression();
            RunLinuxSecretServiceRegression();
        }

        Console.WriteLine("PASS: signed and malformed download URLs are safely logged.");
        Console.WriteLine("PASS: Steam discovery validates complete secondary libraries and rejects incomplete or traversing installs.");
        Console.WriteLine("PASS: remote images enforce supported format, response-byte, and decoded-pixel limits.");
        Console.WriteLine("PASS: user-scoped credential references round-trip, legacy AES envelopes remain readable, and unsafe legacy passwords require re-entry.");
        Console.WriteLine("PASS: settings preserve legacy values until explicit re-entry and then store only opaque vault references.");
        if (OperatingSystem.IsLinux())
        {
            Console.WriteLine("PASS: existing v3 AES keys remain readable from Linux Secret Service without key migration.");
            Console.WriteLine("PASS: Linux credential values persist through the current user's Secret Service.");
        }
        return 0;
    }

    private static void RunCredentialSettingsIntegrationRegression()
    {
        const string legacyFixedCredential = "legacy-fixed-key-format-requires-reentry";
        const string legacyNexusKey = "synthetic legacy Nexus API key";
        const string newNexusKey = "synthetic replacement Nexus API key";
        const string newNolvusPassword = "synthetic replacement Nolvus password";
        const string newMegaPassword = "synthetic replacement Mega password";

        var temporaryRoot = Directory.CreateTempSubdirectory("nknova-credential-settings-");
        var originalDirectory = Environment.CurrentDirectory;
        string scope = "settings-test-" + Guid.NewGuid().ToString("N");
        ILibService? lib = null;
        string? nexusReference = null;
        string? nolvusReference = null;
        string? megaReference = null;
        try
        {
            Environment.CurrentDirectory = temporaryRoot.FullName;
            var iniPath = Path.Combine(temporaryRoot.FullName, SettingsService.IniFile);
            var originalIni = $"[Path]{Environment.NewLine}GamePath=synthetic existing game path{Environment.NewLine}" +
                              $"[Nexus]{Environment.NewLine}ApiKey={legacyNexusKey}{Environment.NewLine}UserAgent=synthetic-agent{Environment.NewLine}" +
                              $"[Nolvus]{Environment.NewLine}Version=v1{Environment.NewLine}UserName=synthetic-old-user{Environment.NewLine}Password={legacyFixedCredential}{Environment.NewLine}" +
                              $"[Mega]{Environment.NewLine}Email=synthetic-old-email{Environment.NewLine}Password={legacyFixedCredential}{Environment.NewLine}" +
                              $"[Process]{Environment.NewLine}Count=4{Environment.NewLine}Retry=7{Environment.NewLine}" +
                              $"[Custom]{Environment.NewLine}KeepValue=preserve-me{Environment.NewLine}";
            File.WriteAllText(iniPath, originalIni);

            var settings = ServiceSingleton.RegisterService<ISettingsService>(new SettingsService());
            var credentialService = new LibService(credentialStorageDirectory: null, secretServiceScope: scope);
            lib = ServiceSingleton.RegisterService<ILibService>(credentialService);
            var globals = new GlobalsService();

            bool legacyNolvusRequiresReentry = false;
            bool legacyMegaRequiresReentry = false;
            try
            {
                _ = credentialService.RetrieveCredential(globals.NolvusPassword);
            }
            catch (LegacyCredentialReentryRequiredException)
            {
                legacyNolvusRequiresReentry = true;
            }

            try
            {
                _ = globals.MegaPassword;
            }
            catch (LegacyCredentialReentryRequiredException)
            {
                legacyMegaRequiresReentry = true;
            }

            if (!string.Equals(globals.NexusApiKey, legacyNexusKey, StringComparison.Ordinal) ||
                !legacyNolvusRequiresReentry || !legacyMegaRequiresReentry)
            {
                throw new Exception("FAILED: production settings did not preserve plaintext Nexus compatibility and safely reject fixed-key legacy passwords.");
            }

            if (!string.Equals(File.ReadAllText(iniPath), originalIni, StringComparison.Ordinal) ||
                Directory.EnumerateFiles(temporaryRoot.FullName, "*.tmp", SearchOption.AllDirectories).Any())
            {
                throw new Exception("FAILED: reading legacy settings rewrote the INI or left temporary files.");
            }

            string newNexusReference = credentialService.StoreCredential(newNexusKey);
            string newNolvusReference = credentialService.StoreCredential(newNolvusPassword);
            nexusReference = newNexusReference;
            nolvusReference = newNolvusReference;
            SettingsService.SaveNolvusCredentialsAtomically(
                iniPath,
                string.Empty,
                newNexusReference,
                "synthetic-new-user",
                newNolvusReference,
                2);

            if (!string.Equals(globals.NexusApiKey, newNexusKey, StringComparison.Ordinal))
                throw new Exception("FAILED: GlobalsService could not retrieve the Nexus API key from the user vault.");
            if (!string.Equals(credentialService.RetrieveCredential(globals.NolvusPassword), newNolvusPassword, StringComparison.Ordinal))
                throw new Exception("FAILED: LibService could not retrieve the Nolvus password from the user vault.");

            string newMegaReference = credentialService.StoreCredential(newMegaPassword);
            megaReference = newMegaReference;
            SettingsService.UpdateIniFileAtomically(iniPath, data =>
                data[GlobalsService.MegaSection][GlobalsService.MegaPswd] = newMegaReference);
            if (!string.Equals(globals.MegaPassword, newMegaPassword, StringComparison.Ordinal))
                throw new Exception("FAILED: GlobalsService could not retrieve the Mega password from the user vault.");

            string persistedIni = File.ReadAllText(iniPath);
            if (persistedIni.Contains(newNexusKey, StringComparison.Ordinal) ||
                persistedIni.Contains(newNolvusPassword, StringComparison.Ordinal) ||
                persistedIni.Contains(newMegaPassword, StringComparison.Ordinal) ||
                persistedIni.Split("nknova:ref:v4:", StringSplitOptions.None).Length < 4)
            {
                throw new Exception("FAILED: saved credentials were not stored as opaque vault references.");
            }

            if (!string.Equals(settings.GetIniValue("Path", "GamePath"), "synthetic existing game path", StringComparison.Ordinal) ||
                !string.Equals(settings.GetIniValue("Process", "Count"), "4", StringComparison.Ordinal) ||
                !string.Equals(settings.GetIniValue("Process", "Retry"), "7", StringComparison.Ordinal) ||
                !string.Equals(settings.GetIniValue("Custom", "KeepValue"), "preserve-me", StringComparison.Ordinal))
            {
                throw new Exception("FAILED: credential re-entry did not preserve unrelated settings.");
            }

            var newInstallIni = Path.Combine(temporaryRoot.FullName, "first-install.ini");
            SettingsService.SaveNolvusCredentialsAtomically(
                newInstallIni,
                "synthetic new install game path",
                newNexusReference,
                "synthetic-first-install-user",
                newNolvusReference,
                2);
            if (!string.Equals(settings.GetIniValue(newInstallIni, "Path", "GamePath"), "synthetic new install game path", StringComparison.Ordinal) ||
                !string.Equals(settings.GetIniValue(newInstallIni, "Nexus", "ApiKey"), newNexusReference, StringComparison.Ordinal) ||
                !string.Equals(settings.GetIniValue(newInstallIni, "Process", "Retry"), "3", StringComparison.Ordinal))
            {
                throw new Exception("FAILED: first-run credential save did not create required settings defaults.");
            }

            if (!OperatingSystem.IsWindows() &&
                File.GetUnixFileMode(iniPath) != (UnixFileMode.UserRead | UnixFileMode.UserWrite))
            {
                throw new Exception("FAILED: credential settings were not persisted with owner-only permissions.");
            }

            // A first-run INI must only contain protected credential envelopes.
            var freshIni = Path.Combine(temporaryRoot.FullName, "fresh-nolvus.ini");
            const string firstRunPassword = "synthetic first-run Nolvus password";
            string freshNexusRef = credentialService.StoreCredential(newNexusKey);
            string freshNolvusRef = credentialService.StoreCredential(firstRunPassword);
            SettingsService.SaveNolvusCredentialsAtomically(
                freshIni, "C:/synthetic/Skyrim", freshNexusRef,
                "synthetic-user", freshNolvusRef, 8);

            string freshContents = File.ReadAllText(freshIni);
            if (freshContents.Contains(newNexusKey, StringComparison.Ordinal) ||
                freshContents.Contains(firstRunPassword, StringComparison.Ordinal) ||
                !string.Equals(settings.GetIniValue(freshIni, "Nolvus", "UserName"), "synthetic-user", StringComparison.Ordinal) ||
                !string.Equals(settings.GetIniValue(freshIni, "Path", "GamePath"), "C:/synthetic/Skyrim", StringComparison.Ordinal) ||
                !string.Equals(lib.RetrieveCredential(settings.GetIniValue(freshIni, "Nexus", "ApiKey")), newNexusKey, StringComparison.Ordinal) ||
                !string.Equals(lib.RetrieveCredential(settings.GetIniValue(freshIni, "Nolvus", "Password")), firstRunPassword, StringComparison.Ordinal))
            {
                throw new Exception("FAILED: first-run settings leaked a secret or lost a protected credential.");
            }

            // Explicit-path updates must use the same atomic, restricted save path.
            settings.StoreIniValue(freshIni, "Nexus", "UserAgent", "NKNOVA isolated test agent");
            if (!string.Equals(settings.GetIniValue(freshIni, "Nexus", "UserAgent"),
                               "NKNOVA isolated test agent", StringComparison.Ordinal))
                throw new Exception("FAILED: explicit-path settings update did not persist.");

            bool iniInjectionRejected = false;
            var rejectedIni = Path.Combine(temporaryRoot.FullName, "rejected.ini");
            try
            {
                SettingsService.SaveNolvusCredentialsAtomically(
                    rejectedIni, "C:/synthetic/Skyrim", freshNexusRef,
                    "synthetic-user" + Environment.NewLine + "[UnexpectedSection]",
                    freshNolvusRef, 8);
            }
            catch (ArgumentException)
            {
                iniInjectionRejected = true;
            }

            if (!iniInjectionRejected || File.Exists(rejectedIni))
                throw new Exception("FAILED: first-run INI accepted a control character or created a partial file.");

            var directoryDestination = Path.Combine(temporaryRoot.FullName, "leave-this-directory");
            Directory.CreateDirectory(directoryDestination);
            bool invalidDestinationRejected = false;
            try
            {
                SettingsService.WriteTextAtomically(directoryDestination, "synthetic data");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                invalidDestinationRejected = true;
            }

            if (!invalidDestinationRejected || !Directory.Exists(directoryDestination) ||
                Directory.EnumerateFiles(temporaryRoot.FullName, ".leave-this-directory.*.tmp").Any())
            {
                throw new Exception("FAILED: atomic settings save did not preserve an invalid destination or clean its temporary file.");
            }

            if (!OperatingSystem.IsWindows())
            {
                var targetIni = Path.Combine(temporaryRoot.FullName, "symlink-target.ini");
                var linkedIni = Path.Combine(temporaryRoot.FullName, "linked.ini");
                File.WriteAllText(targetIni, "synthetic original target");
                File.CreateSymbolicLink(linkedIni, targetIni);
                bool linkRejected = false;
                try
                {
                    SettingsService.WriteTextAtomically(linkedIni, "synthetic replacement");
                }
                catch (IOException)
                {
                    linkRejected = true;
                }

                if (!linkRejected || !string.Equals(File.ReadAllText(targetIni), "synthetic original target", StringComparison.Ordinal))
                    throw new Exception("FAILED: atomic settings save followed a configuration symlink.");

                // Regression: the explicit-file overload must not bypass this guard.
                const string linkedOriginal = "[Nexus]\\nApiKey=synthetic original value\\n";
                File.WriteAllText(targetIni, linkedOriginal.Replace("\\n", Environment.NewLine));
                var explicitPathLinkRejected = false;
                try
                {
                    settings.StoreIniValue(linkedIni, "Nexus", "ApiKey", "synthetic replacement");
                }
                catch (IOException)
                {
                    explicitPathLinkRejected = true;
                }

                if (!explicitPathLinkRejected ||
                    !string.Equals(File.ReadAllText(targetIni),
                                   linkedOriginal.Replace("\\n", Environment.NewLine), StringComparison.Ordinal))
                    throw new Exception("FAILED: explicit-path settings update followed a configuration symlink.");
            }
        }
        finally
        {
            try
            {
                if (lib is not null && !string.IsNullOrEmpty(nexusReference))
                    lib.DeleteCredentialReference(nexusReference);
                if (lib is not null && !string.IsNullOrEmpty(nolvusReference))
                    lib.DeleteCredentialReference(nolvusReference);
                if (lib is not null && !string.IsNullOrEmpty(megaReference))
                    lib.DeleteCredentialReference(megaReference);
            }
            catch
            {
                // Cleanup is limited to the synthetic references created in this test scope.
            }

            Environment.CurrentDirectory = originalDirectory;
            temporaryRoot.Delete(recursive: true);
        }
    }

    private static void RunCredentialProtectionRegression()
    {
        const string testCredential = "synthetic NKNOVA credential regression value";
        var temporaryRoot = Directory.CreateTempSubdirectory("nknova-credentials-");
        string scope = "credential-test-" + Guid.NewGuid().ToString("N");
        var legacyStorageDirectory = Path.Combine(temporaryRoot.FullName, "legacy-file-store");
        ILibService? service = null;
        string? firstReference = null;
        string? secondReference = null;
        try
        {
            service = new LibService(legacyStorageDirectory, scope);

            if (!string.IsNullOrEmpty(service.StoreCredential(string.Empty)) || Directory.Exists(legacyStorageDirectory))
                throw new Exception("FAILED: storing an empty credential created a vault item or file store.");

            firstReference = service.StoreCredential(testCredential);
            if (!firstReference.StartsWith("nknova:ref:v4:", StringComparison.Ordinal) ||
                firstReference.Contains(testCredential, StringComparison.Ordinal) ||
                Directory.Exists(legacyStorageDirectory))
            {
                throw new Exception("FAILED: a new credential was not represented by an opaque OS-vault reference.");
            }

            if (!string.Equals(service.RetrieveCredential(firstReference), testCredential, StringComparison.Ordinal))
                throw new Exception("FAILED: OS-vault credential did not round-trip through LibService.");

            secondReference = service.StoreCredential(testCredential);
            if (string.Equals(firstReference, secondReference, StringComparison.Ordinal))
                throw new Exception("FAILED: repeated credential saves reused the same opaque reference.");

            var reopenedService = new LibService(legacyStorageDirectory, scope);
            if (!string.Equals(reopenedService.RetrieveCredential(firstReference), testCredential, StringComparison.Ordinal))
                throw new Exception("FAILED: a new LibService instance could not retrieve the persisted OS-vault value.");

            char replacement = firstReference[^1] == '0' ? '1' : '0';
            var tamperedReference = firstReference[..^1] + replacement;
            var tamperingRejected = false;
            try
            {
                _ = reopenedService.RetrieveCredential(tamperedReference);
            }
            catch (CryptographicException)
            {
                tamperingRejected = true;
            }

            if (!tamperingRejected)
                throw new Exception("FAILED: modified opaque credential reference was accepted.");

            var malformedEnvelopeRejected = false;
            try
            {
                _ = reopenedService.RetrieveCredential("nknova:ref:v4:" + scope + ":not-a-guid");
            }
            catch (CryptographicException)
            {
                malformedEnvelopeRejected = true;
            }

            if (!malformedEnvelopeRejected)
                throw new Exception("FAILED: malformed credential reference was accepted.");

            var missingReference = "nknova:ref:v4:" + scope + ":" + Guid.NewGuid().ToString("N");
            var missingReferenceRejected = false;
            try
            {
                _ = reopenedService.RetrieveCredential(missingReference);
            }
            catch (CryptographicException)
            {
                missingReferenceRejected = true;
            }

            if (!missingReferenceRejected)
                throw new Exception("FAILED: missing OS-vault credential reference did not fail closed.");

            var missingLegacyDirectory = Path.Combine(temporaryRoot.FullName, "missing-legacy-aes-key");
            var missingLegacyEnvelope = "nknova:v2:" + Convert.ToBase64String(new byte[28]);
            var missingLegacyKeyRejected = false;
            try
            {
                _ = new LibService(missingLegacyDirectory, scope).RetrieveCredential(missingLegacyEnvelope);
            }
            catch (CryptographicException ex) when (ex.Message.Contains("key is missing", StringComparison.OrdinalIgnoreCase))
            {
                missingLegacyKeyRejected = true;
            }

            if (!missingLegacyKeyRejected || Directory.Exists(missingLegacyDirectory))
                throw new Exception("FAILED: missing legacy AES key was not rejected without creating storage.");

            var legacyValueRejected = false;
            try
            {
                _ = reopenedService.RetrieveCredential("legacy-fixed-key-format-requires-reentry");
            }
            catch (LegacyCredentialReentryRequiredException)
            {
                legacyValueRejected = true;
            }

            if (!legacyValueRejected)
                throw new Exception("FAILED: insecure fixed-key legacy credential was not rejected for re-entry.");

            RunLegacyAesEnvelopeRegression(temporaryRoot.FullName, scope, version: 2);
            RunLegacyAesEnvelopeRegression(temporaryRoot.FullName, scope, version: 3);
            RunInvalidLegacyKeySizeRegression(temporaryRoot.FullName, scope);
            RunLegacyCredentialSymlinkRegression(temporaryRoot.FullName, scope);

            if (Directory.Exists(legacyStorageDirectory))
                throw new Exception("FAILED: new v4 credentials were written outside the OS vault.");
        }
        finally
        {
            try
            {
                if (service is not null && !string.IsNullOrEmpty(firstReference))
                    service.DeleteCredentialReference(firstReference);
                if (service is not null && !string.IsNullOrEmpty(secondReference))
                    service.DeleteCredentialReference(secondReference);
            }
            catch
            {
                // Cleanup is limited to the synthetic references created in this test scope.
            }
            temporaryRoot.Delete(recursive: true);
        }
    }

    private static void RunLegacyAesEnvelopeRegression(string temporaryRoot, string credentialScope, int version)
    {
        var legacyDirectory = Path.Combine(temporaryRoot, $"legacy-v{version}-key");
        Directory.CreateDirectory(legacyDirectory);
        byte[] key = RandomNumberGenerator.GetBytes(32);
        try
        {
            File.WriteAllBytes(Path.Combine(legacyDirectory, "credentials.key"), key);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(Path.Combine(legacyDirectory, "credentials.key"), UnixFileMode.UserRead | UnixFileMode.UserWrite);

            string expected = $"synthetic v{version} credential compatibility value";
            string legacyValue = CreateLegacyAesEnvelope(key, expected, version);
            var service = new LibService(legacyDirectory, credentialScope);
            string actual = service.RetrieveCredential(legacyValue);
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
                throw new Exception($"FAILED: legacy v{version} AES-GCM credential could not be read.");

            byte[] envelope = Convert.FromBase64String(legacyValue[$"nknova:v{version}:".Length..]);
            envelope[12] ^= 0x01;
            string tampered = $"nknova:v{version}:" + Convert.ToBase64String(envelope);
            CryptographicOperations.ZeroMemory(envelope);
            bool rejected = false;
            try
            {
                _ = service.RetrieveCredential(tampered);
            }
            catch (CryptographicException)
            {
                rejected = true;
            }

            if (!rejected)
                throw new Exception($"FAILED: tampered legacy v{version} envelope was accepted.");

            if (!OperatingSystem.IsWindows())
            {
                const UnixFileMode groupOrOther = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                                                   UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
                if ((File.GetUnixFileMode(legacyDirectory) & groupOrOther) != 0 ||
                    (File.GetUnixFileMode(Path.Combine(legacyDirectory, "credentials.key")) & groupOrOther) != 0)
                {
                    throw new Exception("FAILED: legacy key-file read did not restrict storage to the current user.");
                }
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static void RunInvalidLegacyKeySizeRegression(string temporaryRoot, string credentialScope)
    {
        var invalidKeyDirectory = Path.Combine(temporaryRoot, "invalid-legacy-key");
        Directory.CreateDirectory(invalidKeyDirectory);
        File.WriteAllBytes(Path.Combine(invalidKeyDirectory, "credentials.key"), new byte[65 * 1024]);
        string envelope = "nknova:v2:" + Convert.ToBase64String(new byte[28]);
        bool invalidKeyRejected = false;
        try
        {
            _ = new LibService(invalidKeyDirectory, credentialScope).RetrieveCredential(envelope);
        }
        catch (CryptographicException)
        {
            invalidKeyRejected = true;
        }

        if (!invalidKeyRejected)
            throw new Exception("FAILED: oversized legacy credential key file was accepted.");
    }

    private static void RunLegacyCredentialSymlinkRegression(string temporaryRoot, string credentialScope)
    {
        string testRoot = Path.Combine(temporaryRoot, "legacy-symlink-regression");
        string targetDirectory = Path.Combine(testRoot, "target-directory");
        string linkedDirectory = Path.Combine(testRoot, "linked-directory");
        string keyDirectory = Path.Combine(testRoot, "key-directory");
        string externalKeyPath = Path.Combine(testRoot, "external-credentials.key");
        string linkedKeyPath = Path.Combine(keyDirectory, "credentials.key");
        Directory.CreateDirectory(targetDirectory);
        Directory.CreateDirectory(keyDirectory);

        byte[] key = RandomNumberGenerator.GetBytes(32);
        try
        {
            string expectedValue = "synthetic legacy credential symlink test";
            string legacyEnvelope = CreateLegacyAesEnvelope(key, expectedValue, version: 2);
            string targetKeyPath = Path.Combine(targetDirectory, "credentials.key");
            File.WriteAllBytes(targetKeyPath, key);
            File.WriteAllBytes(externalKeyPath, key);
            File.WriteAllBytes(linkedKeyPath, key);

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(targetKeyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                File.SetUnixFileMode(externalKeyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                File.SetUnixFileMode(linkedKeyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            try
            {
                Directory.CreateSymbolicLink(linkedDirectory, targetDirectory);
            }
            catch (Exception exception) when (OperatingSystem.IsWindows() && IsWindowsSymlinkPrivilegeError(exception))
            {
                Console.WriteLine("SKIP: Windows runner does not grant symbolic-link creation privilege; Credential Manager/DPAPI regressions still run.");
                return;
            }

            AssertLegacyKeyLinkRejected(linkedDirectory, legacyEnvelope, "linked credential directory");

            File.Delete(linkedKeyPath);
            try
            {
                File.CreateSymbolicLink(linkedKeyPath, externalKeyPath);
            }
            catch (Exception exception) when (OperatingSystem.IsWindows() && IsWindowsSymlinkPrivilegeError(exception))
            {
                Console.WriteLine("SKIP: Windows runner does not grant file-symlink creation privilege; legacy key-directory symlink rejection passed.");
                return;
            }

            AssertLegacyKeyLinkRejected(keyDirectory, legacyEnvelope, "linked credentials.key file");

            byte[] targetBytes = File.ReadAllBytes(targetKeyPath);
            byte[] externalBytes = File.ReadAllBytes(externalKeyPath);
            try
            {
                if (!CryptographicOperations.FixedTimeEquals(targetBytes, key) ||
                    !CryptographicOperations.FixedTimeEquals(externalBytes, key))
                {
                    throw new Exception("FAILED: reading a linked legacy credential key modified its target.");
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(targetBytes);
                CryptographicOperations.ZeroMemory(externalBytes);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static void AssertLegacyKeyLinkRejected(string storageDirectory, string legacyEnvelope, string description)
    {
        bool rejected = false;
        try
        {
            _ = new LibService(storageDirectory).RetrieveCredential(legacyEnvelope);
        }
        catch (CryptographicException)
        {
            rejected = true;
        }

        if (!rejected)
            throw new Exception($"FAILED: legacy credential storage accepted a {description}.");
    }

    private static bool IsWindowsSymlinkPrivilegeError(Exception exception)
    {
        const int ErrorPrivilegeNotHeld = 1314;
        return exception is UnauthorizedAccessException ||
               (exception is IOException or System.ComponentModel.Win32Exception) &&
               (exception.HResult & 0xffff) == ErrorPrivilegeNotHeld;
    }

    private static string CreateLegacyAesEnvelope(byte[] key, string value, int version)
    {
        const int nonceSize = 12;
        const int tagSize = 16;
        byte[] plaintext = Encoding.UTF8.GetBytes(value);
        byte[] nonce = RandomNumberGenerator.GetBytes(nonceSize);
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[tagSize];
        try
        {
            using var aes = new AesGcm(key, tagSize);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, Encoding.UTF8.GetBytes($"NKNOVA user credential envelope v{version}"));
            var envelope = new byte[nonceSize + tagSize + ciphertext.Length];
            Buffer.BlockCopy(nonce, 0, envelope, 0, nonceSize);
            Buffer.BlockCopy(tag, 0, envelope, nonceSize, tagSize);
            Buffer.BlockCopy(ciphertext, 0, envelope, nonceSize + tagSize, ciphertext.Length);
            return $"nknova:v{version}:" + Convert.ToBase64String(envelope);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(nonce);
            CryptographicOperations.ZeroMemory(ciphertext);
            CryptographicOperations.ZeroMemory(tag);
        }
    }

    private static void RunLinuxSecretServiceRegression()
    {
        string scope = "test-" + Guid.NewGuid().ToString("N");
        ILibService? service = null;
        string? reference = null;
        try
        {
            const string testCredential = "synthetic Linux Secret Service credential";
            service = new LibService(credentialStorageDirectory: null, secretServiceScope: scope);
            reference = service.StoreCredential(testCredential);
            if (!reference.StartsWith("nknova:ref:v4:", StringComparison.Ordinal) ||
                reference.Contains(testCredential, StringComparison.Ordinal))
            {
                throw new Exception("FAILED: Linux Secret Service credential did not use an opaque v4 reference.");
            }

            var reopenedService = new LibService(credentialStorageDirectory: null, secretServiceScope: scope);
            if (!string.Equals(reopenedService.RetrieveCredential(reference), testCredential, StringComparison.Ordinal))
                throw new Exception("FAILED: Linux Secret Service credential did not persist across LibService instances.");
        }
        finally
        {
            if (service is not null && !string.IsNullOrEmpty(reference))
            {
                try { service.DeleteCredentialReference(reference); }
                catch { /* Cleanup is limited to the synthetic reference created in this test scope. */ }
            }
        }
    }

    private static void RunLinuxLegacyV3SecretServiceRegression()
    {
        string scope = "legacy-v3-test-" + Guid.NewGuid().ToString("N");
        byte[] key = RandomNumberGenerator.GetBytes(32);
        try
        {
            RunSecretTool(
                "store",
                new[]
                {
                    "--label", "NKNOVA legacy v3 test key",
                    "application", "nknova",
                    "purpose", "credential-encryption-key",
                    "format", "3",
                    "scope", scope
                },
                Encoding.ASCII.GetBytes(Convert.ToBase64String(key)));

            const string expected = "synthetic previous-v3 credential";
            string legacyV3Value = CreateLegacyAesEnvelope(key, expected, version: 3);
            var service = new LibService(credentialStorageDirectory: null, secretServiceScope: scope);
            if (!string.Equals(service.RetrieveCredential(legacyV3Value), expected, StringComparison.Ordinal))
                throw new Exception("FAILED: prior v3 credential key could not be read from Linux Secret Service.");
        }
        finally
        {
            try
            {
                RunSecretTool(
                    "clear",
                    new[]
                    {
                        "application", "nknova",
                        "purpose", "credential-encryption-key",
                        "format", "3",
                        "scope", scope
                    },
                    standardInput: null);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }
    }

    private static void RunSecretTool(string operation, string[] arguments, byte[]? standardInput)
    {
        var startInfo = new ProcessStartInfo("secret-tool")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = standardInput is not null
        };
        startInfo.ArgumentList.Add(operation);
        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new Exception("FAILED: could not run the synthetic Secret Service regression helper.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        if (standardInput is not null)
        {
            process.StandardInput.BaseStream.Write(standardInput);
            process.StandardInput.Close();
        }

        if (!process.WaitForExit(15000))
        {
            process.Kill(entireProcessTree: true);
            throw new Exception("FAILED: synthetic Secret Service regression command timed out.");
        }

        Task.WaitAll(output, error);
        if (process.ExitCode != 0)
            throw new Exception("FAILED: synthetic Secret Service regression command failed.");
    }

    private static void RunSteamLibraryDetectionRegression()
    {
        var temporaryRoot = Directory.CreateTempSubdirectory("nknova-steam-detection-");
        try
        {
            var steamRoot = Path.Combine(temporaryRoot.FullName, "steam");
            var libraryRoot = Path.Combine(temporaryRoot.FullName, "library");
            var libraryFolders = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            var manifest = Path.Combine(libraryRoot, "steamapps", "appmanifest_489830.acf");
            var gameDirectory = Path.Combine(libraryRoot, "steamapps", "common", "Skyrim Special Edition");

            Directory.CreateDirectory(Path.GetDirectoryName(libraryFolders)!);
            Directory.CreateDirectory(Path.GetDirectoryName(manifest)!);
            Directory.CreateDirectory(gameDirectory);

            var escapedLibraryPath = libraryRoot.Replace("\\", "\\\\", StringComparison.Ordinal);
            File.WriteAllText(libraryFolders,
                $"\"libraryfolders\" {{ \"0\" {{ \"path\" \"{escapedLibraryPath}\" }} }}");
            File.WriteAllText(manifest,
                "\"AppState\" { \"installdir\" \"Skyrim Special Edition\" }");

            var service = new GameService(new[] { "relative-root-must-be-ignored", steamRoot });
            if (!string.IsNullOrEmpty(service.GetSkyrimSEDirectory()))
                throw new Exception("FAILED: an incomplete Steam manifest was reported as a usable game installation.");

            File.WriteAllText(Path.Combine(gameDirectory, "SkyrimSE.exe"), "test fixture only");
            var detected = service.GetSkyrimSEDirectory();
            var expected = Path.GetFullPath(gameDirectory);
            if (!string.Equals(detected, expected,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                throw new Exception($"FAILED: Steam library detection returned '{detected}', expected '{expected}'.");
            }

            var escapedGameDirectory = Path.Combine(libraryRoot, "escaped-game");
            Directory.CreateDirectory(escapedGameDirectory);
            File.WriteAllText(Path.Combine(escapedGameDirectory, "SkyrimSE.exe"), "test fixture only");
            File.WriteAllText(manifest,
                "\"AppState\" { \"installdir\" \"../../escaped-game\" }");
            if (!string.IsNullOrEmpty(service.GetSkyrimSEDirectory()))
                throw new Exception("FAILED: a traversing Steam installdir was accepted.");
        }
        finally
        {
            temporaryRoot.Delete(recursive: true);
        }
    }

    private static void RunRemoteImageLoaderRegression()
    {
        var service = new LibService();
        var pngBytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVQI12P4//8/AAX+Av7czFnnAAAAAElFTkSuQmCC");
        var supportedImageResponse = StartLocalImageServer(pngBytes);
        using (var image = service.GetImageFromUrl(supportedImageResponse.Url))
        {
            if (image.Width != 1 || image.Height != 1)
                throw new Exception("FAILED: supported PNG image did not decode through LibService.");
        }
        supportedImageResponse.ServerTask.GetAwaiter().GetResult();

        var unsupportedImageResponse = StartLocalImageServer(new byte[] { (byte)'I', (byte)'I', 42, 0 });
        AssertRemoteImageRejected(
            () => service.GetImageFromWebStream(unsupportedImageResponse.Url),
            "unsupported TIFF signature");
        unsupportedImageResponse.ServerTask.GetAwaiter().GetResult();

        var oversizedImageResponse = StartLocalImageServer(
            Array.Empty<byte>(),
            contentLength: (16 * 1024 * 1024) + 1,
            sendBody: false);
        AssertRemoteImageRejected(
            () => service.GetImageFromUrl(oversizedImageResponse.Url),
            "oversized Content-Length");
        oversizedImageResponse.ServerTask.GetAwaiter().GetResult();

        var streamedOversizedImageResponse = StartLocalImageServer(
            new byte[(16 * 1024 * 1024) + 1],
            sendBody: true,
            includeContentLength: false);
        AssertRemoteImageRejected(
            () => service.GetImageFromUrl(streamedOversizedImageResponse.Url),
            "oversized response without Content-Length");
        streamedOversizedImageResponse.ServerTask.GetAwaiter().GetResult();

        var oversizedDimensionsResponse = StartLocalImageServer(CreateOversizedRle8Bmp());
        AssertRemoteImageRejected(
            () => service.GetImageFromUrl(oversizedDimensionsResponse.Url),
            "image over the pixel limit");
        oversizedDimensionsResponse.ServerTask.GetAwaiter().GetResult();
    }

    private static byte[] CreateOversizedRle8Bmp()
    {
        const int width = 7000;
        const int height = 7000;
        const int bitmapHeaderSize = 14 + 40 + (256 * 4);
        using var encodedPixels = new MemoryStream();
        for (var row = 0; row < height; row++)
        {
            var remaining = width;
            while (remaining > 0)
            {
                var runLength = Math.Min(byte.MaxValue, remaining);
                encodedPixels.WriteByte((byte)runLength);
                encodedPixels.WriteByte(0);
                remaining -= runLength;
            }

            encodedPixels.WriteByte(0);
            encodedPixels.WriteByte(0);
        }

        encodedPixels.WriteByte(0);
        encodedPixels.WriteByte(1);
        var pixelData = encodedPixels.ToArray();
        var bitmap = new byte[bitmapHeaderSize + pixelData.Length];
        bitmap[0] = (byte)'B';
        bitmap[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(bitmap.AsSpan(2, 4), bitmap.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bitmap.AsSpan(10, 4), bitmapHeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(bitmap.AsSpan(14, 4), 40);
        BinaryPrimitives.WriteInt32LittleEndian(bitmap.AsSpan(18, 4), width);
        BinaryPrimitives.WriteInt32LittleEndian(bitmap.AsSpan(22, 4), height);
        BinaryPrimitives.WriteInt16LittleEndian(bitmap.AsSpan(26, 2), 1);
        BinaryPrimitives.WriteInt16LittleEndian(bitmap.AsSpan(28, 2), 8);
        BinaryPrimitives.WriteInt32LittleEndian(bitmap.AsSpan(30, 4), 1);
        BinaryPrimitives.WriteInt32LittleEndian(bitmap.AsSpan(34, 4), pixelData.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bitmap.AsSpan(46, 4), 256);
        pixelData.CopyTo(bitmap, bitmapHeaderSize);
        return bitmap;
    }

    private static (string Url, Task ServerTask) StartLocalImageServer(
        byte[] body,
        long? contentLength = null,
        bool sendBody = true,
        bool includeContentLength = true)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        var url = $"http://127.0.0.1:{endpoint.Port}/image";
        var serverTask = Task.Run(async () =>
        {
            try
            {
                using var client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);

                string? line;
                do
                {
                    line = await reader.ReadLineAsync().ConfigureAwait(false);
                } while (!string.IsNullOrEmpty(line));

                var responseLength = contentLength ?? body.LongLength;
                var lengthHeader = includeContentLength ? $"Content-Length: {responseLength}\r\n" : string.Empty;
                var headers = Encoding.ASCII.GetBytes(
                    $"HTTP/1.1 200 OK\r\nContent-Type: application/octet-stream\r\n{lengthHeader}Connection: close\r\n\r\n");
                await stream.WriteAsync(headers).ConfigureAwait(false);
                if (sendBody && body.Length > 0)
                {
                    try
                    {
                        await stream.WriteAsync(body).ConfigureAwait(false);
                        await stream.FlushAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex) when (!includeContentLength && (ex is IOException or SocketException))
                    {
                        // The bounded client can close the connection after reaching its read limit.
                    }
                }
            }
            finally
            {
                listener.Stop();
            }
        });

        return (url, serverTask);
    }

    private static void AssertRemoteImageRejected(Func<IDisposable> loadImage, string scenario)
    {
        IDisposable? image = null;
        var rejected = false;
        try
        {
            image = loadImage();
        }
        catch (InvalidDataException)
        {
            rejected = true;
        }
        finally
        {
            image?.Dispose();
        }

        if (!rejected)
            throw new Exception($"FAILED: remote image loader accepted {scenario}.");
    }

    private static string CaptureDownloadUrlLog(string url)
    {
        var originalOut = Console.Out;
        using var captured = new StringWriter();
        Console.SetOut(captured);
        try
        {
            // Exercise the production logging entry point without making a network request.
            BaseFileDownloader.LogDownloadUrl(url);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        return captured.ToString();
    }
}
