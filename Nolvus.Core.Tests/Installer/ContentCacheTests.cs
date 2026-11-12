using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using NKNOVA.Installer.Core;
using Xunit;

namespace Nolvus.Core.Tests.Installer
{
    public class ContentCacheTests : IDisposable
    {
        private readonly string _testCacheDir;

        public ContentCacheTests()
        {
            _testCacheDir = Path.Combine(Path.GetTempPath(), "nknova-test-cache-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testCacheDir);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_testCacheDir))
                    Directory.Delete(_testCacheDir, recursive: true);
            }
            catch { }
        }

        [Fact]
        public void StoreStream_valid_sha256_succeeds_and_verifies()
        {
            var cache = new ContentCache(_testCacheDir);
            var content = "This is verified content for the NKNOVA declarative installer test."u8.ToArray();
            var expectedHash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

            using var memStream = new MemoryStream(content);
            var path = cache.StoreStream(memStream, expectedHash);

            Assert.True(File.Exists(path));
            Assert.True(cache.HasValid(expectedHash));
            Assert.Equal(path, cache.GetArchivePath(expectedHash));
        }

        [Fact]
        public void StoreStream_with_mismatched_sha256_throws_and_cleans_up_temp_file()
        {
            var cache = new ContentCache(_testCacheDir);
            var content = "Some content"u8.ToArray();
            var bogusHash = "0000000000000000000000000000000000000000000000000000000000000000";

            using var memStream = new MemoryStream(content);
            Assert.Throws<InvalidDataException>(() => cache.StoreStream(memStream, bogusHash));

            // Ensure no leftover temp files or false cache files exist
            Assert.False(cache.HasValid(bogusHash));
            var remainingFiles = Directory.GetFiles(_testCacheDir);
            Assert.Empty(remainingFiles);
        }

        [Fact]
        public void GetArchivePath_rejects_invalid_hex_length()
        {
            var cache = new ContentCache(_testCacheDir);
            Assert.Throws<ArgumentException>(() => cache.GetArchivePath("short"));
        }
    }
}
