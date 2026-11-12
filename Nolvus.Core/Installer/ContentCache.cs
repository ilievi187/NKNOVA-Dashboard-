using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace NKNOVA.Installer.Core
{
    public interface IContentCache
    {
        bool HasValid(string sha256);
        string GetArchivePath(string sha256);
        string StoreStream(Stream input, string expectedSha256);
        Task<string> StoreStreamAsync(Stream input, string expectedSha256, CancellationToken cancellationToken = default);
        bool TryVerifyFile(string filePath, string expectedSha256);
    }

    /// <summary>
    /// Content-addressed archive storage. Prevents corruption by writing to .tmp files,
    /// verifying cryptographic SHA-256 integrity, and atomically publishing to content-addressed paths.
    /// </summary>
    public sealed class ContentCache : IContentCache
    {
        private readonly string _cacheDirectory;

        public ContentCache(string? cacheDirectory = null)
        {
            if (string.IsNullOrWhiteSpace(cacheDirectory))
            {
                var xdgCache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
                var baseDir = !string.IsNullOrWhiteSpace(xdgCache)
                    ? xdgCache
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");
                _cacheDirectory = Path.Combine(baseDir, "nknova", "archives");
            }
            else
            {
                _cacheDirectory = Path.GetFullPath(cacheDirectory);
            }

            Directory.CreateDirectory(_cacheDirectory);
        }

        public string CacheDirectory => _cacheDirectory;

        public bool HasValid(string sha256)
        {
            if (string.IsNullOrWhiteSpace(sha256) || sha256.Length != 64)
                return false;

            var target = GetArchivePath(sha256);
            if (!File.Exists(target))
                return false;

            return TryVerifyFile(target, sha256);
        }

        public string GetArchivePath(string sha256)
        {
            var normalized = sha256.Trim().ToLowerInvariant();
            if (normalized.Length != 64 || !RegexPatternMatch(normalized))
                throw new ArgumentException("Invalid SHA256 format.", nameof(sha256));

            return Path.Combine(_cacheDirectory, normalized + ".archive");
        }

        public string StoreStream(Stream input, string expectedSha256)
        {
            ArgumentNullException.ThrowIfNull(input);
            var normalizedExpected = expectedSha256.Trim().ToLowerInvariant();
            var targetPath = GetArchivePath(normalizedExpected);

            if (File.Exists(targetPath) && TryVerifyFile(targetPath, normalizedExpected))
                return targetPath;

            var tempPath = Path.Combine(_cacheDirectory, $".tmp-{Guid.NewGuid():N}.download");
            try
            {
                using (var sha = SHA256.Create())
                using (var output = File.Open(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var cryptoStream = new CryptoStream(output, sha, CryptoStreamMode.Write))
                {
                    input.CopyTo(cryptoStream);
                    cryptoStream.FlushFinalBlock();
                    var actual = Convert.ToHexString(sha.Hash!).ToLowerInvariant();
                    if (!string.Equals(actual, normalizedExpected, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException($"SHA256 mismatch for archive: expected {normalizedExpected}, got {actual}");
                    }
                }

                if (File.Exists(targetPath))
                    File.Delete(targetPath);

                File.Move(tempPath, targetPath);
                return targetPath;
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { }
                }
            }
        }

        public async Task<string> StoreStreamAsync(Stream input, string expectedSha256, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            var normalizedExpected = expectedSha256.Trim().ToLowerInvariant();
            var targetPath = GetArchivePath(normalizedExpected);

            if (File.Exists(targetPath) && TryVerifyFile(targetPath, normalizedExpected))
                return targetPath;

            var tempPath = Path.Combine(_cacheDirectory, $".tmp-{Guid.NewGuid():N}.download");
            try
            {
                using (var sha = SHA256.Create())
                using (var output = File.Open(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var cryptoStream = new CryptoStream(output, sha, CryptoStreamMode.Write))
                {
                    await input.CopyToAsync(cryptoStream, cancellationToken).ConfigureAwait(false);
                    cryptoStream.FlushFinalBlock();
                    var actual = Convert.ToHexString(sha.Hash!).ToLowerInvariant();
                    if (!string.Equals(actual, normalizedExpected, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException($"SHA256 mismatch for archive: expected {normalizedExpected}, got {actual}");
                    }
                }

                if (File.Exists(targetPath))
                    File.Delete(targetPath);

                File.Move(tempPath, targetPath);
                return targetPath;
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { }
                }
            }
        }

        public bool TryVerifyFile(string filePath, string expectedSha256)
        {
            try
            {
                if (!File.Exists(filePath)) return false;
                using var stream = File.OpenRead(filePath);
                var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
                return string.Equals(actual, expectedSha256.Trim().ToLowerInvariant(), StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool RegexPatternMatch(string hex)
        {
            foreach (char c in hex)
            {
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                    return false;
            }
            return true;
        }
    }
}
