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

        var secretValues = new[]
        {
            "secret-password",
            "SECRET_API_KEY",
            "SECRET_SIGNATURE",
            "private/mod.zip",
            "downloads.example.test/private",
            "api_key",
            "signature",
            "expires",
            "fragment",
            "999999999"
        };

        // Exercise the production logging path without making any network request.
        var originalOut = Console.Out;
        var captured = new StringWriter();
        Console.SetOut(captured);
        try
        {
            BaseFileDownloader.LogDownloadUrl(signedUrl);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        var logs = captured.ToString();

        foreach (var secret in secretValues)
        {
            if (logs.Contains(secret, StringComparison.Ordinal))
                throw new Exception("FAILED: signed URL secret reached logs: " + secret);
        }

        // The log may contain only the host; ensure no userinfo/path/query/fragment leaked.
        if (!logs.Contains("downloads.example.test", StringComparison.Ordinal))
            throw new Exception("FAILED: safe host missing from logs");

        if (logs.Contains("user:", StringComparison.Ordinal) ||
            logs.Contains("?", StringComparison.Ordinal) ||
            logs.Contains("#", StringComparison.Ordinal) ||
            logs.Contains("@", StringComparison.Ordinal))
        {
            throw new Exception("FAILED: unsafe URL delimiter reached logs");
        }

        Console.WriteLine("PASS: BaseFileDownloader redacts signed download URLs.");
        return 0;
    }
}
