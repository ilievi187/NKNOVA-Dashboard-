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
            RunLinuxSecretServiceRegression();

        Console.WriteLine("PASS: signed and malformed download URLs are safely logged.");
        Console.WriteLine("PASS: Steam discovery validates complete secondary libraries and rejects incomplete or traversing installs.");
        Console.WriteLine("PASS: remote images enforce supported format, response-byte, and decoded-pixel limits.");
        Console.WriteLine("PASS: user-scoped credentials encrypt, persist, authenticate, and retain legacy read compatibility.");
        Console.WriteLine("PASS: settings preserve legacy credentials and round-trip newly protected Nexus keys.");
        if (OperatingSystem.IsLinux())
            Console.WriteLine("PASS: Linux credential keys persist through the current user's Secret Service.");
        return 0;
    }

    private static void RunCredentialSettingsIntegrationRegression()
    {
        const string legacyCiphertext = "AI0pbfv2QbwhDFKWDdiA0sPZIpmNfOyo+2fJQyiSKH1CiSG+akfx1g==";
        const string legacyValue = "NKNOVA legacy migration test only";
        const string legacyNexusKey = "synthetic legacy Nexus API key";
        const string newNexusKey = "synthetic replacement Nexus API key";
        const string newMegaPassword = "synthetic replacement Mega password";

        var temporaryRoot = Directory.CreateTempSubdirectory("nknova-credential-settings-");
        var originalDirectory = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = temporaryRoot.FullName;
            var iniPath = Path.Combine(temporaryRoot.FullName, SettingsService.IniFile);
            var originalIni = $"[Nexus]{Environment.NewLine}ApiKey={legacyNexusKey}{Environment.NewLine}" +
                              $"[Nolvus]{Environment.NewLine}Password={legacyCiphertext}{Environment.NewLine}" +
                              $"[Mega]{Environment.NewLine}Password={legacyCiphertext}{Environment.NewLine}";
            File.WriteAllText(iniPath, originalIni);

            const string storageDirectory = "credential-store";
            var settings = ServiceSingleton.RegisterService<ISettingsService>(new SettingsService());
            var lib = ServiceSingleton.RegisterService<ILibService>(new LibService(storageDirectory));
            var globals = new GlobalsService();

            if (!string.Equals(globals.NexusApiKey, legacyNexusKey, StringComparison.Ordinal) ||
                !string.Equals(lib.DecryptString(globals.NolvusPassword), legacyValue, StringComparison.Ordinal) ||
                !string.Equals(globals.MegaPassword, legacyValue, StringComparison.Ordinal))
            {
                throw new Exception("FAILED: production settings services could not read legacy dummy credentials.");
            }

            if (!string.Equals(File.ReadAllText(iniPath), originalIni, StringComparison.Ordinal) ||
                Directory.Exists(storageDirectory))
            {
                throw new Exception("FAILED: reading legacy settings rewrote the INI or created credential storage.");
            }

            string protectedNexusKey = lib.EncryptString(newNexusKey);
            settings.StoreIniValue("Nexus", "ApiKey", protectedNexusKey);
            if (!string.Equals(globals.NexusApiKey, newNexusKey, StringComparison.Ordinal))
                throw new Exception("FAILED: GlobalsService could not read a newly protected Nexus API key.");

            string protectedMegaPassword = lib.EncryptString(newMegaPassword);
            SettingsService.UpdateIniFileAtomically(iniPath, data =>
                data[GlobalsService.MegaSection][GlobalsService.MegaPswd] = protectedMegaPassword);
            if (!string.Equals(globals.MegaPassword, newMegaPassword, StringComparison.Ordinal))
                throw new Exception("FAILED: GlobalsService could not read a newly protected Mega password.");

            string persistedIni = File.ReadAllText(iniPath);
            if (persistedIni.Contains(newNexusKey, StringComparison.Ordinal) ||
                persistedIni.Contains(newMegaPassword, StringComparison.Ordinal) ||
                persistedIni.Split("nknova:v3:", StringSplitOptions.None).Length < 3)
            {
                throw new Exception("FAILED: newly saved Nexus or Mega credentials were not stored as protected envelopes.");
            }

            if (!OperatingSystem.IsWindows() &&
                File.GetUnixFileMode(iniPath) != (UnixFileMode.UserRead | UnixFileMode.UserWrite))
            {
                throw new Exception("FAILED: credential settings were not persisted with owner-only permissions.");
            }

            // A first-run INI must only contain protected credential envelopes.
            var freshIni = Path.Combine(temporaryRoot.FullName, "fresh-nolvus.ini");
            const string firstRunPassword = "synthetic first-run Nolvus password";
            SettingsService.WriteFreshSettingsAtomically(
                freshIni, "C:/synthetic/Skyrim", newNexusKey,
                "synthetic-user", firstRunPassword, 8, lib);

            string freshContents = File.ReadAllText(freshIni);
            if (freshContents.Contains(newNexusKey, StringComparison.Ordinal) ||
                freshContents.Contains(firstRunPassword, StringComparison.Ordinal) ||
                !string.Equals(settings.GetIniValue(freshIni, "Nolvus", "UserName"), "synthetic-user", StringComparison.Ordinal) ||
                !string.Equals(settings.GetIniValue(freshIni, "Path", "GamePath"), "C:/synthetic/Skyrim", StringComparison.Ordinal) ||
                !string.Equals(lib.DecryptString(settings.GetIniValue(freshIni, "Nexus", "ApiKey")), newNexusKey, StringComparison.Ordinal) ||
                !string.Equals(lib.DecryptString(settings.GetIniValue(freshIni, "Nolvus", "Password")), firstRunPassword, StringComparison.Ordinal))
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
                SettingsService.WriteFreshSettingsAtomically(
                    rejectedIni, "C:/synthetic/Skyrim", newNexusKey,
                    "synthetic-user" + Environment.NewLine + "[UnexpectedSection]",
                    firstRunPassword, 8, lib);
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
            catch (IOException)
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
            Environment.CurrentDirectory = originalDirectory;
            temporaryRoot.Delete(recursive: true);
        }
    }

    private static void RunCredentialProtectionRegression()
    {
        const string testCredential = "synthetic NKNOVA credential regression value";
        const string legacyCiphertext = "AI0pbfv2QbwhDFKWDdiA0sPZIpmNfOyo+2fJQyiSKH1CiSG+akfx1g==";
        const string expectedLegacyValue = "NKNOVA legacy migration test only";

        var temporaryRoot = Directory.CreateTempSubdirectory("nknova-credentials-");
        try
        {
            var storageDirectory = Path.Combine(temporaryRoot.FullName, "profile", "credentials");
            var service = new LibService(storageDirectory);

            if (!string.IsNullOrEmpty(service.EncryptString(string.Empty)) || Directory.Exists(storageDirectory))
                throw new Exception("FAILED: encrypting an empty credential created storage or returned data.");

            var protectedValue = service.EncryptString(testCredential);
            if (!protectedValue.StartsWith("nknova:v3:", StringComparison.Ordinal) ||
                protectedValue.Contains(testCredential, StringComparison.Ordinal))
            {
                throw new Exception("FAILED: a newly saved credential was not protected in the expected envelope.");
            }

            if (!string.Equals(service.DecryptString(protectedValue), testCredential, StringComparison.Ordinal))
                throw new Exception("FAILED: protected credential did not round-trip through LibService.");

            var secondProtectedValue = service.EncryptString(testCredential);
            if (string.Equals(protectedValue, secondProtectedValue, StringComparison.Ordinal))
                throw new Exception("FAILED: repeated credential encryption reused the same nonce/envelope.");

            var reopenedService = new LibService(storageDirectory);
            if (!string.Equals(reopenedService.DecryptString(protectedValue), testCredential, StringComparison.Ordinal))
                throw new Exception("FAILED: a new LibService instance could not read the persisted credential key.");

            var tamperedBytes = Convert.FromBase64String(protectedValue["nknova:v3:".Length..]);
            tamperedBytes[12] ^= 0x01;
            var tamperedValue = "nknova:v3:" + Convert.ToBase64String(tamperedBytes);
            CryptographicOperations.ZeroMemory(tamperedBytes);
            var tamperingRejected = false;
            try
            {
                _ = reopenedService.DecryptString(tamperedValue);
            }
            catch (CryptographicException)
            {
                tamperingRejected = true;
            }

            if (!tamperingRejected)
                throw new Exception("FAILED: modified credential envelope was accepted.");

            var malformedEnvelopeRejected = false;
            try
            {
                _ = reopenedService.DecryptString("nknova:v3:not-base64!");
            }
            catch (CryptographicException)
            {
                malformedEnvelopeRejected = true;
            }

            if (!malformedEnvelopeRejected)
                throw new Exception("FAILED: malformed protected credential text was accepted.");

            RunLegacyV2EnvelopeRegression(temporaryRoot.FullName);

            var missingKeyDirectory = Path.Combine(temporaryRoot.FullName, "missing-key", "credentials");
            var missingKeyRejected = false;
            try
            {
                _ = new LibService(missingKeyDirectory).DecryptString(protectedValue);
            }
            catch (CryptographicException ex) when (ex.Message.Contains("key is missing", StringComparison.OrdinalIgnoreCase))
            {
                missingKeyRejected = true;
            }

            if (!missingKeyRejected || Directory.Exists(missingKeyDirectory))
                throw new Exception("FAILED: a missing protected-credential key was not rejected without creating storage.");

            var invalidKeyDirectory = Path.Combine(temporaryRoot.FullName, "invalid-key");
            Directory.CreateDirectory(invalidKeyDirectory);
            File.WriteAllBytes(Path.Combine(invalidKeyDirectory, "credentials.key"), new byte[65 * 1024]);
            var invalidKeyRejected = false;
            try
            {
                _ = new LibService(invalidKeyDirectory).DecryptString(protectedValue);
            }
            catch (CryptographicException)
            {
                invalidKeyRejected = true;
            }

            if (!invalidKeyRejected)
                throw new Exception("FAILED: invalid persisted key size was accepted.");

            if (OperatingSystem.IsLinux())
            {
                var absentSecretServiceScope = "missing-" + Guid.NewGuid().ToString("N");
                var absentSecretServiceKeyRejected = false;
                try
                {
                    _ = new LibService(credentialStorageDirectory: null, secretServiceScope: absentSecretServiceScope)
                        .DecryptString(protectedValue);
                }
                catch (CryptographicException ex) when (ex.Message.Contains("key is missing", StringComparison.OrdinalIgnoreCase))
                {
                    absentSecretServiceKeyRejected = true;
                }

                if (!absentSecretServiceKeyRejected)
                    throw new Exception("FAILED: missing Linux Secret Service key did not fail closed.");
            }

            var legacyDirectory = Path.Combine(temporaryRoot.FullName, "legacy-read-only", "credentials");
            var legacyService = new LibService(legacyDirectory);
            if (!string.Equals(legacyService.DecryptString(legacyCiphertext), expectedLegacyValue, StringComparison.Ordinal))
                throw new Exception("FAILED: legacy credential value could not be read.");

            if (Directory.Exists(legacyDirectory))
                throw new Exception("FAILED: reading a legacy credential silently created or migrated credential storage.");

            // Legacy read compatibility must reject malformed or oversized inputs without
            // writing a key or touching the existing settings file.
            string[] invalidLegacyValues =
            {
                "bad",
                "%%%%",
                Convert.ToBase64String(new byte[7]),
                new string('A', 24 * 1024 + 4)
            };
            foreach (string invalidLegacyValue in invalidLegacyValues)
            {
                bool rejected = false;
                try
                {
                    _ = legacyService.DecryptString(invalidLegacyValue);
                }
                catch (CryptographicException)
                {
                    rejected = true;
                }

                if (!rejected)
                    throw new Exception("FAILED: malformed or oversized legacy credential was accepted.");
                if (Directory.Exists(legacyDirectory))
                    throw new Exception("FAILED: invalid legacy credential created key storage.");
            }

            if (!OperatingSystem.IsWindows())
            {
                const UnixFileMode groupOrOther = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                                                   UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
                var directoryMode = File.GetUnixFileMode(storageDirectory);
                var keyMode = File.GetUnixFileMode(Path.Combine(storageDirectory, "credentials.key"));
                if ((directoryMode & groupOrOther) != 0 || (keyMode & groupOrOther) != 0)
                    throw new Exception("FAILED: credential directory or key permissions are not restricted to the current user.");

                var linkedDirectoryTarget = Path.Combine(temporaryRoot.FullName, "linked-target");
                var linkedDirectory = Path.Combine(temporaryRoot.FullName, "linked-credentials");
                Directory.CreateDirectory(linkedDirectoryTarget);
                Directory.CreateSymbolicLink(linkedDirectory, linkedDirectoryTarget);
                var linkedDirectoryRejected = false;
                try
                {
                    _ = new LibService(linkedDirectory).EncryptString(testCredential);
                }
                catch (CryptographicException)
                {
                    linkedDirectoryRejected = true;
                }

                if (!linkedDirectoryRejected || Directory.EnumerateFileSystemEntries(linkedDirectoryTarget).Any())
                    throw new Exception("FAILED: credential storage accepted a linked directory or wrote through it.");
            }
            else
            {
                var persistedKey = File.ReadAllBytes(Path.Combine(storageDirectory, "credentials.key"));
                if (persistedKey.Length == 32)
                    throw new Exception("FAILED: Windows credential key was stored without per-user DPAPI protection.");
                CryptographicOperations.ZeroMemory(persistedKey);
            }
        }
        finally
        {
            temporaryRoot.Delete(recursive: true);
        }
    }

    private static void RunLegacyV2EnvelopeRegression(string temporaryRoot)
    {
        var legacyDirectory = Path.Combine(temporaryRoot, "legacy-v2-key");
        Directory.CreateDirectory(legacyDirectory);
        byte[] key = RandomNumberGenerator.GetBytes(32);
        try
        {
            File.WriteAllBytes(Path.Combine(legacyDirectory, "credentials.key"), key);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(Path.Combine(legacyDirectory, "credentials.key"), UnixFileMode.UserRead | UnixFileMode.UserWrite);

            const string expected = "synthetic v2 credential compatibility value";
            string legacyValue = CreateLegacyV2Envelope(key, expected);
            string actual = new LibService(legacyDirectory).DecryptString(legacyValue);
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
                throw new Exception("FAILED: legacy v2 AES-GCM credential could not be read.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static string CreateLegacyV2Envelope(byte[] key, string value)
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
            aes.Encrypt(nonce, plaintext, ciphertext, tag, Encoding.UTF8.GetBytes("NKNOVA user credential envelope v2"));
            var envelope = new byte[nonceSize + tagSize + ciphertext.Length];
            Buffer.BlockCopy(nonce, 0, envelope, 0, nonceSize);
            Buffer.BlockCopy(tag, 0, envelope, nonceSize, tagSize);
            Buffer.BlockCopy(ciphertext, 0, envelope, nonceSize + tagSize, ciphertext.Length);
            return "nknova:v2:" + Convert.ToBase64String(envelope);
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
        try
        {
            const string testCredential = "synthetic Linux Secret Service credential";
            var service = new LibService(credentialStorageDirectory: null, secretServiceScope: scope);
            string protectedValue = service.EncryptString(testCredential);
            if (!protectedValue.StartsWith("nknova:v3:", StringComparison.Ordinal) ||
                protectedValue.Contains(testCredential, StringComparison.Ordinal))
            {
                throw new Exception("FAILED: Linux Secret Service credential did not use the protected v3 envelope.");
            }

            var reopenedService = new LibService(credentialStorageDirectory: null, secretServiceScope: scope);
            if (!string.Equals(reopenedService.DecryptString(protectedValue), testCredential, StringComparison.Ordinal))
                throw new Exception("FAILED: Linux Secret Service credential did not persist across LibService instances.");
        }
        finally
        {
            ClearLinuxSecretServiceTestScope(scope);
        }
    }

    private static void ClearLinuxSecretServiceTestScope(string scope)
    {
        var startInfo = new ProcessStartInfo("secret-tool")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("clear");
        startInfo.ArgumentList.Add("application");
        startInfo.ArgumentList.Add("nknova");
        startInfo.ArgumentList.Add("purpose");
        startInfo.ArgumentList.Add("credential-encryption-key");
        startInfo.ArgumentList.Add("format");
        startInfo.ArgumentList.Add("3");
        startInfo.ArgumentList.Add("scope");
        startInfo.ArgumentList.Add(scope);

        using var process = Process.Start(startInfo)
            ?? throw new Exception("FAILED: could not clean up the synthetic Secret Service test item.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(15000))
        {
            process.Kill(entireProcessTree: true);
            throw new Exception("FAILED: timed out cleaning up the synthetic Secret Service test item.");
        }

        Task.WaitAll(output, error);
        if (process.ExitCode != 0)
            throw new Exception("FAILED: could not clean up the synthetic Secret Service test item.");
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
