using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Nolvus.Services.Files;
using Nolvus.Services.Game;
using Nolvus.Services.Lib;

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

        Console.WriteLine("PASS: signed and malformed download URLs are safely logged.");
        Console.WriteLine("PASS: Steam discovery validates complete secondary libraries and rejects incomplete or traversing installs.");
        Console.WriteLine("PASS: remote images require supported signatures and are bounded by response size.");
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

    private static void RunRemoteImageLoaderRegression()
    {
        var service = new LibService();
        var pngBytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jhccAAAAASUVORK5CYII=");
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
