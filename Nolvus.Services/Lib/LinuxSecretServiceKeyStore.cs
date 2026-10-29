using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Nolvus.Services.Lib
{
    /// <summary>
    /// Reads only the v3 AES key created by the earlier, unmerged credential implementation.
    /// New credential writes use PlatformCredentialStore and never create this key item.
    /// </summary>
    internal sealed class LinuxSecretServiceKeyStore
    {
        private const int KeySize = 32;
        private const int ProcessTimeoutMilliseconds = 12000;
        private const string ApplicationAttribute = "nknova";
        private const string PurposeAttribute = "credential-encryption-key";
        private const string FormatVersionAttribute = "3";

        private readonly string _scope;

        public LinuxSecretServiceKeyStore(string scope)
        {
            if (string.IsNullOrWhiteSpace(scope) || scope.Length > 96)
                throw new ArgumentException("Secret Service scope must contain 1-96 safe characters.", nameof(scope));

            foreach (char character in scope)
            {
                if (!(char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-'))
                    throw new ArgumentException("Secret Service scope contains an unsupported character.", nameof(scope));
            }

            _scope = scope;
        }

        public byte[] ReadKey()
        {
            CommandResult result = Execute("lookup", BuildAttributes());
            if (result.ExitCode == 1 && string.IsNullOrWhiteSpace(result.StandardError))
                throw new CryptographicException("The Linux v3 credential key is missing from Secret Service. Existing settings were not changed.");
            if (result.ExitCode != 0)
                throw SecretServiceUnavailable();

            string encodedKey = result.StandardOutput.TrimEnd('\r', '\n');
            byte[] key;
            try
            {
                key = Convert.FromBase64String(encodedKey);
            }
            catch (FormatException)
            {
                throw new CryptographicException("Linux Secret Service returned an invalid v3 credential key.");
            }

            if (key.Length != KeySize)
            {
                CryptographicOperations.ZeroMemory(key);
                throw new CryptographicException("Linux Secret Service returned a v3 credential key with an invalid length.");
            }

            return key;
        }

        private string[] BuildAttributes() => new[]
        {
            "application", ApplicationAttribute,
            "purpose", PurposeAttribute,
            "format", FormatVersionAttribute,
            "scope", _scope
        };

        private static CommandResult Execute(string operation, string[] arguments)
        {
            var startInfo = new ProcessStartInfo("secret-tool")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            startInfo.ArgumentList.Add(operation);
            foreach (string argument in arguments)
                startInfo.ArgumentList.Add(argument);

            using var process = new Process { StartInfo = startInfo };
            try
            {
                if (!process.Start())
                    throw SecretServiceUnavailable();
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or FileNotFoundException)
            {
                throw SecretServiceUnavailable();
            }

            Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
            Task<string> errorTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(ProcessTimeoutMilliseconds))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit();
                }
                catch (InvalidOperationException)
                {
                    // The process exited between the timeout and the termination attempt.
                }

                throw new CryptographicException("Linux Secret Service timed out while reading a legacy v3 key.");
            }

            Task.WaitAll(outputTask, errorTask);
            return new CommandResult(process.ExitCode, outputTask.Result, errorTask.Result);
        }

        private static CryptographicException SecretServiceUnavailable() =>
            new("Linux Secret Service is unavailable. The prior v3 credential key cannot be read.");

        private sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError);
    }
}
