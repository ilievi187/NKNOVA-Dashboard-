using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Nolvus.Services.Lib
{
    /// <summary>
    /// Stores new credentials in the current user's Windows Credential Manager or freedesktop Secret Service.
    /// Settings files contain only random references, never the credential value or an encrypted copy of it.
    /// </summary>
    internal sealed class PlatformCredentialStore
    {
        public const string ReferencePrefix = "nknova:ref:v4:";
        private const string ApplicationAttribute = "nknova";
        private const string PurposeAttribute = "account-credential";
        private const string FormatVersionAttribute = "4";
        private const int MaximumCredentialBytes = 4096;
        private readonly string _scope;

        public PlatformCredentialStore(string scope = "default")
        {
            if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
                throw new PlatformNotSupportedException("User credential storage is supported on Windows and Linux only.");
            _scope = ValidateScope(scope);
        }

        public string Store(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            if (Encoding.UTF8.GetByteCount(value) > MaximumCredentialBytes)
                throw new CryptographicException("Credential is too large for the supported user vault.");

            string id = Guid.NewGuid().ToString("N");
            if (OperatingSystem.IsWindows())
                WindowsCredentialManager.Store(TargetName(id), value);
            else if (OperatingSystem.IsLinux())
                LinuxSecretService.Store(_scope, id, value);
            else
                throw new PlatformNotSupportedException("User credential storage is supported on Windows and Linux only.");

            return CreateReference(_scope, id);
        }

        public string Retrieve(string reference)
        {
            if (!TryParseReference(reference, out string scope, out string id))
                throw new CryptographicException("Saved credential reference is invalid.");
            if (!string.Equals(scope, _scope, StringComparison.Ordinal))
                throw new CryptographicException("Saved credential belongs to a different user-store scope.");

            string value;
            if (OperatingSystem.IsWindows())
                value = WindowsCredentialManager.Retrieve(TargetName(id));
            else if (OperatingSystem.IsLinux())
                value = LinuxSecretService.Retrieve(_scope, id);
            else
                throw new PlatformNotSupportedException("User credential storage is supported on Windows and Linux only.");

            if (Encoding.UTF8.GetByteCount(value) > MaximumCredentialBytes)
                throw new CryptographicException("Credential from the user vault exceeds the supported size.");
            return value;
        }

        public void Delete(string reference)
        {
            if (!TryParseReference(reference, out string scope, out string id) ||
                !string.Equals(scope, _scope, StringComparison.Ordinal))
            {
                throw new CryptographicException("Saved credential reference is invalid.");
            }

            if (OperatingSystem.IsWindows())
                WindowsCredentialManager.Delete(TargetName(id));
            else if (OperatingSystem.IsLinux())
                LinuxSecretService.Delete(_scope, id);
            else
                throw new PlatformNotSupportedException("User credential storage is supported on Windows and Linux only.");
        }

        public static bool IsReference(string? value) =>
            value?.StartsWith(ReferencePrefix, StringComparison.Ordinal) == true;

        private string TargetName(string id) => $"NKNOVA:credential:v4:{_scope}:{id}";

        private static string CreateReference(string scope, string id) => $"{ReferencePrefix}{scope}:{id}";

        private static bool TryParseReference(string? value, out string scope, out string id)
        {
            scope = string.Empty;
            id = string.Empty;
            if (!IsReference(value))
                return false;

            string[] parts = value![ReferencePrefix.Length..].Split(':');
            if (parts.Length != 2 || !IsValidScope(parts[0]) || !Guid.TryParseExact(parts[1], "N", out _))
                return false;

            scope = parts[0];
            id = parts[1];
            return true;
        }

        private static string ValidateScope(string scope)
        {
            if (!IsValidScope(scope))
                throw new ArgumentException("Credential scope must contain 1-96 safe characters.", nameof(scope));
            return scope;
        }

        private static bool IsValidScope(string? scope)
        {
            if (string.IsNullOrWhiteSpace(scope) || scope.Length > 96)
                return false;

            foreach (char character in scope)
            {
                if (!(char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-'))
                    return false;
            }

            return true;
        }

        private static class LinuxSecretService
        {
            private const int ProcessTimeoutMilliseconds = 12000;

            public static void Store(string scope, string id, string value)
            {
                byte[] plaintext = Encoding.UTF8.GetBytes(value);
                byte[] encoded = new byte[Base64.GetMaxEncodedToUtf8Length(plaintext.Length)];
                try
                {
                    Base64.EncodeToUtf8(plaintext, encoded, out _, out int encodedLength);
                    CommandResult result = Execute(
                        "store",
                        BuildAttributes(scope, id, includeLabel: true),
                        encoded.AsSpan(0, encodedLength));
                    if (result.ExitCode != 0)
                        throw SecretStoreUnavailable();
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(plaintext);
                    CryptographicOperations.ZeroMemory(encoded);
                }
            }

            public static string Retrieve(string scope, string id)
            {
                CommandResult result = Execute("lookup", BuildAttributes(scope, id), ReadOnlySpan<byte>.Empty);
                if (result.ExitCode == 1 && string.IsNullOrWhiteSpace(result.StandardError))
                    throw MissingCredential();
                if (result.ExitCode != 0)
                    throw SecretStoreUnavailable();

                string encoded = result.StandardOutput.TrimEnd('\r', '\n');
                byte[] plaintext;
                try
                {
                    plaintext = Convert.FromBase64String(encoded);
                }
                catch (FormatException)
                {
                    throw new CryptographicException("Linux Secret Service returned an invalid credential value.");
                }

                try
                {
                    return Encoding.UTF8.GetString(plaintext);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(plaintext);
                }
            }

            public static void Delete(string scope, string id)
            {
                CommandResult result = Execute("clear", BuildAttributes(scope, id), ReadOnlySpan<byte>.Empty);
                if (result.ExitCode != 0 && !(result.ExitCode == 1 && string.IsNullOrWhiteSpace(result.StandardError)))
                    throw SecretStoreUnavailable();
            }

            private static string[] BuildAttributes(string scope, string id, bool includeLabel = false)
            {
                var attributes = new List<string>();
                if (includeLabel)
                {
                    attributes.Add("--label");
                    attributes.Add("NKNOVA account credential");
                }

                attributes.AddRange(new[]
                {
                    "application", ApplicationAttribute,
                    "purpose", PurposeAttribute,
                    "format", FormatVersionAttribute,
                    "scope", scope,
                    "id", id
                });
                return attributes.ToArray();
            }

            private static CommandResult Execute(string operation, string[] arguments, ReadOnlySpan<byte> standardInput)
            {
                var startInfo = new ProcessStartInfo("secret-tool")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = !standardInput.IsEmpty,
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
                        throw SecretStoreUnavailable();
                }
                catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or FileNotFoundException)
                {
                    throw SecretStoreUnavailable();
                }

                Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
                Task<string> errorTask = process.StandardError.ReadToEndAsync();
                if (!standardInput.IsEmpty)
                {
                    process.StandardInput.BaseStream.Write(standardInput);
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

                    throw SecretStoreUnavailable();
                }

                Task.WaitAll(outputTask, errorTask);
                return new CommandResult(process.ExitCode, outputTask.Result, errorTask.Result);
            }

            private static CryptographicException MissingCredential() =>
                new("The saved Linux credential is missing from Secret Service; existing settings were not changed.");

            private static CryptographicException SecretStoreUnavailable() =>
                new("Linux Secret Service is unavailable. Install and unlock a Secret Service-compatible keyring, then retry. Existing settings were not changed.");

            private sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError);
        }

        private static class WindowsCredentialManager
        {
            private const uint GenericCredential = 1;
            private const uint PersistLocalMachine = 2;
            private const int ErrorNotFound = 1168;
            private const int MaximumCredentialBlobBytes = 5 * 1024;

            public static void Store(string targetName, string value)
            {
                byte[] plaintext = Encoding.UTF8.GetBytes(value);
                byte[] protectedValue = Array.Empty<byte>();
                IntPtr targetPointer = IntPtr.Zero;
                IntPtr userPointer = IntPtr.Zero;
                IntPtr blobPointer = IntPtr.Zero;
                try
                {
                    protectedValue = WindowsDpapi.Protect(plaintext);
                    if (protectedValue.Length == 0 || protectedValue.Length > MaximumCredentialBlobBytes)
                        throw new CryptographicException("Windows DPAPI returned an invalid credential size.");

                    targetPointer = Marshal.StringToCoTaskMemUni(targetName);
                    userPointer = Marshal.StringToCoTaskMemUni("NKNOVA");
                    blobPointer = Marshal.AllocHGlobal(protectedValue.Length);
                    Marshal.Copy(protectedValue, 0, blobPointer, protectedValue.Length);

                    var credential = new NativeCredential
                    {
                        Type = GenericCredential,
                        TargetName = targetPointer,
                        CredentialBlobSize = (uint)protectedValue.Length,
                        CredentialBlob = blobPointer,
                        Persist = PersistLocalMachine,
                        UserName = userPointer
                    };

                    if (!CredWrite(ref credential, 0))
                        throw new CryptographicException("Windows Credential Manager could not store the current-user credential.");
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(plaintext);
                    CryptographicOperations.ZeroMemory(protectedValue);
                    ZeroUnmanaged(blobPointer, protectedValue.Length);
                    if (blobPointer != IntPtr.Zero)
                        Marshal.FreeHGlobal(blobPointer);
                    if (targetPointer != IntPtr.Zero)
                        Marshal.FreeCoTaskMem(targetPointer);
                    if (userPointer != IntPtr.Zero)
                        Marshal.FreeCoTaskMem(userPointer);
                }
            }

            public static string Retrieve(string targetName)
            {
                if (!CredRead(targetName, GenericCredential, 0, out IntPtr credentialPointer))
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error == ErrorNotFound)
                        throw new CryptographicException("The saved Windows credential is missing; existing settings were not changed.");
                    throw new CryptographicException("Windows Credential Manager could not read the current-user credential.");
                }

                byte[] protectedValue = Array.Empty<byte>();
                byte[]? plaintext = null;
                try
                {
                    var credential = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
                    if (credential.CredentialBlobSize == 0 || credential.CredentialBlobSize > MaximumCredentialBlobBytes)
                        throw new CryptographicException("Windows Credential Manager returned an invalid credential size.");

                    int blobLength = checked((int)credential.CredentialBlobSize);
                    protectedValue = new byte[blobLength];
                    Marshal.Copy(credential.CredentialBlob, protectedValue, 0, protectedValue.Length);
                    plaintext = WindowsDpapi.Unprotect(protectedValue);
                    return Encoding.UTF8.GetString(plaintext);
                }
                finally
                {
                    if (plaintext is not null)
                        CryptographicOperations.ZeroMemory(plaintext);
                    CryptographicOperations.ZeroMemory(protectedValue);
                    CredFree(credentialPointer);
                }
            }

            public static void Delete(string targetName)
            {
                if (!CredDelete(targetName, GenericCredential, 0) && Marshal.GetLastWin32Error() != ErrorNotFound)
                    throw new CryptographicException("Windows Credential Manager could not remove the temporary credential.");
            }

            private static void ZeroUnmanaged(IntPtr pointer, int length)
            {
                if (pointer == IntPtr.Zero)
                    return;
                for (int index = 0; index < length; index++)
                    Marshal.WriteByte(pointer, index, 0);
            }

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            private struct NativeCredential
            {
                public uint Flags;
                public uint Type;
                public IntPtr TargetName;
                public IntPtr Comment;
                public long LastWritten;
                public uint CredentialBlobSize;
                public IntPtr CredentialBlob;
                public uint Persist;
                public uint AttributeCount;
                public IntPtr Attributes;
                public IntPtr TargetAlias;
                public IntPtr UserName;
            }

            [DllImport("advapi32.dll", EntryPoint = "CredWriteW", SetLastError = true, CharSet = CharSet.Unicode)]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static extern bool CredWrite(ref NativeCredential credential, uint flags);

            [DllImport("advapi32.dll", EntryPoint = "CredReadW", SetLastError = true, CharSet = CharSet.Unicode)]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static extern bool CredRead(string targetName, uint type, uint flags, out IntPtr credential);

            [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", SetLastError = true, CharSet = CharSet.Unicode)]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static extern bool CredDelete(string targetName, uint type, uint flags);

            [DllImport("advapi32.dll", ExactSpelling = true)]
            private static extern void CredFree(IntPtr credential);
        }

        private static class WindowsDpapi
        {
            private const uint UiForbidden = 0x1;

            public static byte[] Protect(byte[] plaintext) => Transform(plaintext, protect: true);

            public static byte[] Unprotect(byte[] protectedValue) => Transform(protectedValue, protect: false);

            private static byte[] Transform(byte[] input, bool protect)
            {
                if (!OperatingSystem.IsWindows())
                    throw new PlatformNotSupportedException("Windows DPAPI is available only on Windows.");

                IntPtr inputBuffer = Marshal.AllocHGlobal(input.Length);
                var inputBlob = new DataBlob { Length = input.Length, Data = inputBuffer };
                DataBlob outputBlob = default;
                IntPtr description = IntPtr.Zero;
                try
                {
                    Marshal.Copy(input, 0, inputBuffer, input.Length);
                    bool succeeded = protect
                        ? CryptProtectData(ref inputBlob, "NKNOVA current-user credential", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, UiForbidden, out outputBlob)
                        : CryptUnprotectData(ref inputBlob, out description, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, UiForbidden, out outputBlob);

                    if (!succeeded)
                        throw new CryptographicException("Windows DPAPI could not protect or unlock the current-user credential.");

                    var result = new byte[outputBlob.Length];
                    Marshal.Copy(outputBlob.Data, result, 0, result.Length);
                    return result;
                }
                finally
                {
                    for (int index = 0; index < input.Length; index++)
                        Marshal.WriteByte(inputBuffer, index, 0);
                    Marshal.FreeHGlobal(inputBuffer);

                    if (outputBlob.Data != IntPtr.Zero)
                    {
                        if (!protect)
                        {
                            for (int index = 0; index < outputBlob.Length; index++)
                                Marshal.WriteByte(outputBlob.Data, index, 0);
                        }
                        _ = LocalFree(outputBlob.Data);
                    }

                    if (description != IntPtr.Zero)
                        _ = LocalFree(description);
                }
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct DataBlob
            {
                public int Length;
                public IntPtr Data;
            }

            [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static extern bool CryptProtectData(ref DataBlob input, string description, IntPtr optionalEntropy, IntPtr reserved, IntPtr prompt, uint flags, out DataBlob output);

            [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static extern bool CryptUnprotectData(ref DataBlob input, out IntPtr description, IntPtr optionalEntropy, IntPtr reserved, IntPtr prompt, uint flags, out DataBlob output);

            [DllImport("kernel32.dll", ExactSpelling = true)]
            private static extern IntPtr LocalFree(IntPtr memory);
        }

        private static CryptographicException SecretStoreUnavailable() =>
            new("User credential storage is unavailable on this platform.");
    }
}
