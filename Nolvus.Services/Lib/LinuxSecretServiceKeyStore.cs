using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Nolvus.Services.Lib
{
    /// <summary>
    /// Stores the credential-encryption key in the current user's freedesktop Secret Service.
    /// The key is sent to secret-tool over stdin, never in process arguments or logs.
    /// </summary>
    internal sealed class LinuxSecretServiceKeyStore
    {
        private const int KeySize = 32;
        private const int ProcessTimeoutMilliseconds = 12000;
        private const string ApplicationAttribute = "nknova";
        private const string PurposeAttribute = "credential-encryption-key";
        private const string FormatVersionAttribute = "3";

        private static readonly object ProcessLocalLock = new();
        private readonly string _scope;
        private readonly string _mutexName;

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
            byte[] mutexHash = SHA256.HashData(Encoding.UTF8.GetBytes(Environment.UserName + "\0" + scope));
            try
            {
                _mutexName = "NKNOVA.CredentialKey." + Convert.ToHexString(mutexHash.AsSpan(0, 16));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(mutexHash);
            }
        }

        public byte[] GetOrCreateKey()
        {
            lock (ProcessLocalLock)
            {
                using var mutex = new Mutex(initiallyOwned: false, _mutexName);
                bool ownsMutex = false;
                try
                {
                    try
                    {
                        ownsMutex = mutex.WaitOne(TimeSpan.FromSeconds(10));
                    }
                    catch (AbandonedMutexException)
                    {
                        ownsMutex = true;
                    }

                    if (!ownsMutex)
                        throw new CryptographicException("Timed out waiting for the per-user credential store. No credential was changed.");

                    byte[]? existingKey = ReadStoredKey();
                    if (existingKey is not null)
                        return existingKey;

                    byte[] newKey = RandomNumberGenerator.GetBytes(KeySize);
                    try
                    {
                        StoreKey(newKey);
                        byte[]? storedKey = ReadStoredKey();
                        if (storedKey is null)
                            throw SecretServiceUnavailable();

                        return storedKey;
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(newKey);
                    }
                }
                finally
                {
                    if (ownsMutex)
                        mutex.ReleaseMutex();
                }
            }
        }

        public byte[] ReadKey()
        {
            lock (ProcessLocalLock)
            {
                byte[]? key = ReadStoredKey();
                return key ?? throw new CryptographicException(
                    "The Linux per-user credential key is missing from Secret Service. Existing settings were not changed.");
            }
        }

        private byte[]? ReadStoredKey()
        {
            CommandResult result = Execute("lookup", BuildAttributes(), standardInput: null);
            if (result.ExitCode == 1 && string.IsNullOrWhiteSpace(result.StandardError))
                return null;

            if (result.ExitCode != 0)
                throw SecretServiceUnavailable();

            string encodedKey = result.StandardOutput.TrimEnd('\r', '\n');
            if (encodedKey.Length == 0)
                throw new CryptographicException("Linux Secret Service returned an empty credential key.");

            byte[] key;
            try
            {
                key = Convert.FromBase64String(encodedKey);
            }
            catch (FormatException)
            {
                throw new CryptographicException("Linux Secret Service returned an invalid credential key.");
            }

            if (key.Length != KeySize)
            {
                CryptographicOperations.ZeroMemory(key);
                throw new CryptographicException("Linux Secret Service returned a credential key with an invalid length.");
            }

            return key;
        }

        private void StoreKey(byte[] key)
        {
            string encodedKey = Convert.ToBase64String(key);
            var arguments = new[]
            {
                "--label", "NKNOVA credential-encryption key",
                "application", ApplicationAttribute,
                "purpose", PurposeAttribute,
                "format", FormatVersionAttribute,
                "scope", _scope
            };

            CommandResult result = Execute("store", arguments, encodedKey);
            if (result.ExitCode != 0)
                throw SecretServiceUnavailable();
        }

        private string[] BuildAttributes() => new[]
        {
            "application", ApplicationAttribute,
            "purpose", PurposeAttribute,
            "format", FormatVersionAttribute,
            "scope", _scope
        };

        private static CommandResult Execute(string operation, string[] arguments, string? standardInput)
        {
            var startInfo = new ProcessStartInfo("secret-tool")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = standardInput is not null,
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
            if (standardInput is not null)
            {
                process.StandardInput.Write(standardInput);
                process.StandardInput.Close();
            }

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

                throw new CryptographicException(
                    "Linux Secret Service timed out. Ensure a per-user keyring is available and unlocked; credentials were not changed.");
            }

            Task.WaitAll(outputTask, errorTask);
            return new CommandResult(process.ExitCode, outputTask.Result, errorTask.Result);
        }

        private static CryptographicException SecretServiceUnavailable() =>
            new("Linux Secret Service is unavailable. Install and unlock a Secret Service-compatible keyring, then retry. Credentials were not changed.");

        private sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError);
    }
}
