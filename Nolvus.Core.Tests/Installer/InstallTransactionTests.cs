using System;
using System.IO;
using System.Text;
using NKNOVA.Installer.Core;
using Xunit;

namespace Nolvus.Core.Tests.Installer
{
    public class InstallTransactionTests : IDisposable
    {
        private readonly string _testRoot;
        private readonly string _txRoot;
        private readonly string _targetRoot;

        public InstallTransactionTests()
        {
            _testRoot = Path.Combine(Path.GetTempPath(), "nknova-tx-test-" + Guid.NewGuid().ToString("N"));
            _txRoot = Path.Combine(_testRoot, "tx");
            _targetRoot = Path.Combine(_testRoot, "target");

            Directory.CreateDirectory(_txRoot);
            Directory.CreateDirectory(_targetRoot);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_testRoot))
                    Directory.Delete(_testRoot, recursive: true);
            }
            catch { }
        }

        [Fact]
        public void Staging_files_with_path_traversal_is_rejected()
        {
            using var tx = new InstallTransaction(_txRoot, _targetRoot);
            var content = new MemoryStream(Encoding.UTF8.GetBytes("malicious content"));

            var ex = Assert.Throws<InvalidOperationException>(() =>
                tx.StageFile("skse64", "../../../escaped.dll", content));

            Assert.Contains("path traversal", ex.Message);
        }

        [Fact]
        public void Successful_transaction_stages_commits_and_records_in_journal()
        {
            using (var tx = new InstallTransaction(_txRoot, _targetRoot))
            {
                var content = new MemoryStream(Encoding.UTF8.GetBytes("SKSE test binary"));
                var stagedPath = tx.StageFile("skse64", "skse64_loader.exe", content);
                Assert.True(File.Exists(stagedPath));

                tx.Commit();
                Assert.Equal(TransactionState.Committed, tx.State);
            }

            var committedFile = Path.Combine(_targetRoot, "skse64", "skse64_loader.exe");
            Assert.True(File.Exists(committedFile));
            Assert.Equal("SKSE test binary", File.ReadAllText(committedFile));

            var journal = InstallJournal.Load(Path.Combine(_txRoot, "journal.json"));
            Assert.NotNull(journal);
            Assert.Equal(TransactionState.Committed, journal.State);
        }

        [Fact]
        public void Rollback_cleans_up_staged_and_leaves_target_clean()
        {
            // Pre-seed an existing file in target to ensure rollback never deletes pre-existing files
            var existingDir = Path.Combine(_targetRoot, "existing-mod");
            Directory.CreateDirectory(existingDir);
            var existingFile = Path.Combine(existingDir, "safe.txt");
            File.WriteAllText(existingFile, "pre-existing safe data");

            using (var tx = new InstallTransaction(_txRoot, _targetRoot))
            {
                var content = new MemoryStream(Encoding.UTF8.GetBytes("partial install data"));
                tx.StageFile("partial-mod", "test.bin", content);

                // Explicitly rollback
                tx.Rollback();
                Assert.Equal(TransactionState.RolledBack, tx.State);
            }

            // Target pre-existing file must remain intact
            Assert.True(File.Exists(existingFile));
            Assert.Equal("pre-existing safe data", File.ReadAllText(existingFile));

            // Staged directory must be cleaned up
            var stagesDir = Path.Combine(_txRoot, "stages");
            Assert.False(Directory.Exists(stagesDir));

            // Journal must record RolledBack state
            var journal = InstallJournal.Load(Path.Combine(_txRoot, "journal.json"));
            Assert.NotNull(journal);
            Assert.Equal(TransactionState.RolledBack, journal.State);
        }

        [Fact]
        public void Uncommitted_transaction_disposal_triggers_rollback()
        {
            using (var tx = new InstallTransaction(_txRoot, _targetRoot))
            {
                var content = new MemoryStream(Encoding.UTF8.GetBytes("will be abandoned"));
                tx.StageFile("abandoned-mod", "data.dat", content);
                // No tx.Commit() called; Dispose() called at end of using block
            }

            var journal = InstallJournal.Load(Path.Combine(_txRoot, "journal.json"));
            Assert.NotNull(journal);
            Assert.Equal(TransactionState.RolledBack, journal.State);
        }
    }
}
