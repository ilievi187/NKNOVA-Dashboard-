using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Nolvus.Services.Lib
{
    /// <summary>
    /// Protects newly saved account credentials with a random per-user AES-GCM key.
    /// Existing legacy values are handled by LibService and are never rewritten here.
    /// </summary>
    internal sealed class UserCredentialProtector
    {
        public const string LegacyValuePrefix = "nknova:v2:";
        public const string ValuePrefix = "nknova:v3:";

        private const int KeySize = 32;
        private const int NonceSize = 12;
        private const int TagSize = 16;
        private const int MaximumPersistedKeyBytes = 64 * 1024;
        private const string KeyFileName = "credentials.key";
        private const uint CryptProtectUiForbidden = 0x1;

        private static readonly byte[] LegacyAssociatedData = Encoding.UTF8.GetBytes("NKNOVA user credential envelope v2");
        private static readonly byte[] AssociatedData = Encoding.UTF8.GetBytes("NKNOVA user credential envelope v3");
        private static readonly object KeyFileLock = new();

        private readonly string _directory;
        private readonly string _keyPath;
        private readonly bool _useFileKeyStore;
        private readonly LinuxSecretServiceKeyStore? _linuxSecretServiceKeyStore;
        private readonly Lazy<byte[]> _encryptionKey;

        public UserCredentialProtector(string? storageDirectory = null, string? secretServiceScope = null)
        {
            _useFileKeyStore = !string.IsNullOrWhiteSpace(storageDirectory);
            if (!_useFileKeyStore && !OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
                throw new PlatformNotSupportedException("User-scoped credential storage is supported on Windows and Linux only.");

            string directory = string.IsNullOrWhiteSpace(storageDirectory)
                ? GetDefaultStorageDirectory()
                : storageDirectory;

            _directory = Path.GetFullPath(directory);
            _keyPath = Path.Combine(_directory, KeyFileName);

            if (OperatingSystem.IsLinux() && !_useFileKeyStore)
                _linuxSecretServiceKeyStore = new LinuxSecretServiceKeyStore(secretServiceScope ?? "default");

            _encryptionKey = new Lazy<byte[]>(LoadOrCreateCurrentKey, LazyThreadSafetyMode.ExecutionAndPublication);
        }

        public static bool IsProtectedValue(string? value) =>
            value?.StartsWith(ValuePrefix, StringComparison.Ordinal) == true ||
            value?.StartsWith(LegacyValuePrefix, StringComparison.Ordinal) == true;

        public string Protect(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            byte[] plaintext = Encoding.UTF8.GetBytes(value);
            byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
            byte[] ciphertext = new byte[plaintext.Length];
            byte[] tag = new byte[TagSize];
            try
            {
                using var aes = new AesGcm(_encryptionKey.Value, TagSize);
                aes.Encrypt(nonce, plaintext, ciphertext, tag, AssociatedData);

                byte[] envelope = new byte[NonceSize + TagSize + ciphertext.Length];
                Buffer.BlockCopy(nonce, 0, envelope, 0, NonceSize);
                Buffer.BlockCopy(tag, 0, envelope, NonceSize, TagSize);
                Buffer.BlockCopy(ciphertext, 0, envelope, NonceSize + TagSize, ciphertext.Length);
                return ValuePrefix + Convert.ToBase64String(envelope);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
                CryptographicOperations.ZeroMemory(ciphertext);
                CryptographicOperations.ZeroMemory(tag);
                CryptographicOperations.ZeroMemory(nonce);
            }
        }

        public string Unprotect(string value)
        {
            if (!IsProtectedValue(value))
                throw new CryptographicException("Unsupported protected credential format.");

            bool isLegacyEnvelope = value.StartsWith(LegacyValuePrefix, StringComparison.Ordinal);
            string prefix = isLegacyEnvelope ? LegacyValuePrefix : ValuePrefix;
            byte[] envelope;
            try
            {
                envelope = Convert.FromBase64String(value[prefix.Length..]);
            }
            catch (FormatException)
            {
                throw new CryptographicException("The protected credential envelope is malformed.");
            }

            if (envelope.Length < NonceSize + TagSize)
            {
                CryptographicOperations.ZeroMemory(envelope);
                throw new CryptographicException("The protected credential envelope is incomplete.");
            }

            byte[]? key = null;
            byte[]? plaintext = null;
            try
            {
                key = isLegacyEnvelope ? ReadExistingKey() : ReadExistingCurrentKey();
                plaintext = new byte[envelope.Length - NonceSize - TagSize];
                using var aes = new AesGcm(key, TagSize);
                aes.Decrypt(
                    envelope.AsSpan(0, NonceSize),
                    envelope.AsSpan(NonceSize + TagSize),
                    envelope.AsSpan(NonceSize, TagSize),
                    plaintext,
                    isLegacyEnvelope ? LegacyAssociatedData : AssociatedData
                );
                return Encoding.UTF8.GetString(plaintext);
            }
            finally
            {
                if (key is not null)
                    CryptographicOperations.ZeroMemory(key);
                if (plaintext is not null)
                    CryptographicOperations.ZeroMemory(plaintext);
                CryptographicOperations.ZeroMemory(envelope);
            }
        }

        private byte[] LoadOrCreateCurrentKey()
        {
            if (OperatingSystem.IsLinux() && !_useFileKeyStore)
                return _linuxSecretServiceKeyStore!.GetOrCreateKey();

            return LoadOrCreateKey();
        }

        private byte[] ReadExistingCurrentKey()
        {
            if (OperatingSystem.IsLinux() && !_useFileKeyStore)
                return _linuxSecretServiceKeyStore!.ReadKey();

            return ReadExistingKey();
        }

        private byte[] LoadOrCreateKey()
        {
            lock (KeyFileLock)
            {
                RejectLink(_directory, isDirectory: true);
                Directory.CreateDirectory(_directory);
                RejectLink(_directory, isDirectory: true);
                RestrictDirectoryPermissions(_directory);

                RejectLink(_keyPath, isDirectory: false);
                if (File.Exists(_keyPath))
                    return ReadExistingKey();

                byte[] key = RandomNumberGenerator.GetBytes(KeySize);
                string temporaryKeyPath = Path.Combine(
                    _directory,
                    $".{KeyFileName}.{Guid.NewGuid():N}.tmp");
                try
                {
                    byte[] persistedKey = ProtectKeyForStorage(key);
                    try
                    {
                        using var stream = new FileStream(
                            temporaryKeyPath,
                            FileMode.CreateNew,
                            FileAccess.Write,
                            FileShare.None,
                            4096,
                            FileOptions.WriteThrough
                        );
                        RestrictFilePermissions(temporaryKeyPath);
                        stream.Write(persistedKey, 0, persistedKey.Length);
                        stream.Flush(flushToDisk: true);
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(persistedKey);
                    }

                    try
                    {
                        File.Move(temporaryKeyPath, _keyPath);
                    }
                    catch (IOException) when (File.Exists(_keyPath))
                    {
                        CryptographicOperations.ZeroMemory(key);
                        return ReadExistingKey();
                    }

                    RestrictFilePermissions(_keyPath);
                    return key;
                }
                catch
                {
                    CryptographicOperations.ZeroMemory(key);
                    throw;
                }
                finally
                {
                    try
                    {
                        File.Delete(temporaryKeyPath);
                    }
                    catch (IOException)
                    {
                        // The atomically moved key is already durable; an orphaned temp is user-only protected.
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Do not mask the original key creation result with cleanup failure.
                    }
                }
            }
        }

        private byte[] ReadExistingKey()
        {
            RejectLink(_directory, isDirectory: true);
            RejectLink(_keyPath, isDirectory: false);
            if (!File.Exists(_keyPath))
                throw new CryptographicException("The per-user credential key is missing. Existing settings were not changed.");

            var keyFile = new FileInfo(_keyPath);
            if (keyFile.Length <= 0 || keyFile.Length > MaximumPersistedKeyBytes)
                throw new CryptographicException("The per-user credential key file has an invalid size. Existing settings were not changed.");

            byte[] persistedKey = File.ReadAllBytes(_keyPath);
            byte[] key;
            try
            {
                // Before DPAPI wrapping was added, the v2 Windows key was stored as a raw 32-byte file.
                // Permit that legacy format for read-only compatibility, but never write it again.
                key = OperatingSystem.IsWindows() && persistedKey.Length == KeySize
                    ? (byte[])persistedKey.Clone()
                    : UnprotectKeyForCurrentUser(persistedKey);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(persistedKey);
            }

            if (key.Length != KeySize)
            {
                CryptographicOperations.ZeroMemory(key);
                throw new CryptographicException("The per-user credential key has an invalid length. Existing settings were not changed.");
            }

            RestrictDirectoryPermissions(_directory);
            RestrictFilePermissions(_keyPath);
            return key;
        }

        private static void RejectLink(string path, bool isDirectory)
        {
            if (isDirectory)
            {
                RejectDirectoryPathLinks(path);
                return;
            }

            string directory = Path.GetDirectoryName(path)
                ?? throw new CryptographicException("Credential key path has no parent directory.");
            RejectDirectoryPathLinks(directory);
            RejectLinkTarget(new FileInfo(path));
        }

        private static void RejectDirectoryPathLinks(string path)
        {
            string fullPath = Path.GetFullPath(path);
            string root = Path.GetPathRoot(fullPath)
                ?? throw new CryptographicException("Credential storage path has no filesystem root.");
            string current = root;

            foreach (string component in fullPath[root.Length..].Split(
                         new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                         StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, component);
                RejectLinkTarget(new DirectoryInfo(current));
            }
        }

        private static void RejectLinkTarget(FileSystemInfo info)
        {
            try
            {
                if (info.LinkTarget is not null)
                    throw new CryptographicException("Credential storage must not use symbolic links or reparse points.");
            }
            catch (FileNotFoundException)
            {
                // A missing leaf is expected while preparing a first-use key path.
            }
            catch (DirectoryNotFoundException)
            {
                // A missing parent is expected while preparing a first-use key path.
            }
        }

        private static byte[] ProtectKeyForStorage(byte[] key) =>
            OperatingSystem.IsWindows() ? TransformWithDpapi(key, protect: true) : (byte[])key.Clone();

        private static byte[] UnprotectKeyForCurrentUser(byte[] persistedKey) =>
            OperatingSystem.IsWindows() ? TransformWithDpapi(persistedKey, protect: false) : (byte[])persistedKey.Clone();

        private static byte[] TransformWithDpapi(byte[] input, bool protect)
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
                    ? CryptProtectData(
                        ref inputBlob,
                        "NKNOVA per-user credential key",
                        IntPtr.Zero,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        CryptProtectUiForbidden,
                        out outputBlob)
                    : CryptUnprotectData(
                        ref inputBlob,
                        out description,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        CryptProtectUiForbidden,
                        out outputBlob);

                if (!succeeded)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not protect or unlock the per-user credential key.");

                var output = new byte[outputBlob.Length];
                Marshal.Copy(outputBlob.Data, output, 0, output.Length);
                return output;
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

        private static void RestrictDirectoryPermissions(string directory)
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    directory,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                );
            }
        }

        private static void RestrictFilePermissions(string path)
        {
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        private static string GetDefaultStorageDirectory()
        {
            if (OperatingSystem.IsWindows())
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrWhiteSpace(localAppData))
                    throw new IOException("The Windows per-user application data directory is unavailable.");

                return Path.Combine(localAppData, "NKNOVA", "Credentials");
            }

            string? xdgConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (!string.IsNullOrWhiteSpace(xdgConfigHome) && Path.IsPathFullyQualified(xdgConfigHome))
                return Path.Combine(xdgConfigHome, "nknova", "credentials");

            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(home))
                throw new IOException("The user profile directory is unavailable for credential storage.");

            return Path.Combine(home, ".config", "nknova", "credentials");
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DataBlob
        {
            public int Length;
            public IntPtr Data;
        }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptProtectData(
            ref DataBlob input,
            string description,
            IntPtr optionalEntropy,
            IntPtr reserved,
            IntPtr prompt,
            uint flags,
            out DataBlob output);

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptUnprotectData(
            ref DataBlob input,
            out IntPtr description,
            IntPtr optionalEntropy,
            IntPtr reserved,
            IntPtr prompt,
            uint flags,
            out DataBlob output);

        [DllImport("kernel32.dll", ExactSpelling = true)]
        private static extern IntPtr LocalFree(IntPtr memory);
    }
}
