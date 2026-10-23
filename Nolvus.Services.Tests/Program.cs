using System;
using System.IO;
using Nolvus.Services.Files;
using Nolvus.Services.Game;

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

        Console.WriteLine("PASS: signed and malformed download URLs are safely logged.");
        Console.WriteLine("PASS: Steam discovery validates complete secondary libraries and rejects incomplete or traversing installs.");
        return 0;
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
