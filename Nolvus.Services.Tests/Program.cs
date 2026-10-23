using System;
using System.IO;
using Nolvus.Services.Files;

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

        Console.WriteLine("PASS: signed and malformed download URLs are safely logged.");
        return 0;
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
