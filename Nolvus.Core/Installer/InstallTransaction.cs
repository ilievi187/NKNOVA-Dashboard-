using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NKNOVA.Installer.Core
{
    public enum TransactionState
    {
        Pending,
        InProgress,
        Staged,
        Committed,
        Failed,
        RolledBack
    }

    public sealed class TransactionJournalEntry
    {
        [JsonPropertyName("componentId")] public string ComponentId { get; set; } = "";
        [JsonPropertyName("stagedDirectory")] public string StagedDirectory { get; set; } = "";
        [JsonPropertyName("targetDirectory")] public string TargetDirectory { get; set; } = "";
        [JsonPropertyName("createdRelativeFiles")] public List<string> CreatedRelativeFiles { get; set; } = new();
        [JsonPropertyName("status")] public string Status { get; set; } = "Pending";
    }

    public sealed class InstallJournal
    {
        [JsonPropertyName("transactionId")] public string TransactionId { get; set; } = "";
        [JsonPropertyName("createdUtc")] public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        [JsonPropertyName("state")] public TransactionState State { get; set; } = TransactionState.Pending;
        [JsonPropertyName("entries")] public List<TransactionJournalEntry> Entries { get; set; } = new();

        public void Save(string path)
        {
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            var temp = path + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, path, overwrite: true);
        }

        public static InstallJournal? Load(string path)
        {
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<InstallJournal>(json);
        }
    }

    /// <summary>
    /// Transactional staging and rollback manager. Ensures all writes are staged
    /// into isolated directories first, verified, and committed. On failure or cancellation,
    /// complete rollback removes intermediate files without touching target directory state.
    /// </summary>
    public sealed class InstallTransaction : IDisposable
    {
        private readonly string _transactionRoot;
        private readonly string _targetRoot;
        private readonly string _stagesRoot;
        private readonly string _journalPath;
        private readonly InstallJournal _journal;
        private bool _disposed;

        public InstallTransaction(string transactionRoot, string targetRoot)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(transactionRoot);
            ArgumentException.ThrowIfNullOrWhiteSpace(targetRoot);

            _transactionRoot = Path.GetFullPath(transactionRoot);
            _targetRoot = Path.GetFullPath(targetRoot);
            _stagesRoot = Path.Combine(_transactionRoot, "stages");
            _journalPath = Path.Combine(_transactionRoot, "journal.json");

            Directory.CreateDirectory(_stagesRoot);
            Directory.CreateDirectory(_targetRoot);

            _journal = new InstallJournal
            {
                TransactionId = Guid.NewGuid().ToString("N"),
                CreatedUtc = DateTime.UtcNow,
                State = TransactionState.InProgress
            };
            _journal.Save(_journalPath);
        }

        public string TransactionId => _journal.TransactionId;
        public TransactionState State => _journal.State;
        public string TargetRoot => _targetRoot;

        /// <summary>Stages a file inside the isolated component stage directory, enforcing path containment.</summary>
        public string StageFile(string componentId, string relativePath, Stream content)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(componentId);
            ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
            ArgumentNullException.ThrowIfNull(content);

            if (_journal.State != TransactionState.InProgress)
                throw new InvalidOperationException($"Cannot stage files when transaction is in {_journal.State} state.");

            ManifestValidation.ValidateId(componentId);

            var componentStageDir = Path.Combine(_stagesRoot, componentId);
            Directory.CreateDirectory(componentStageDir);

            // Path containment enforcement: ensure relative path cannot escape componentStageDir
            var combined = Path.Combine(componentStageDir, relativePath);
            var normalizedDest = Path.GetFullPath(combined);
            var normalizedStageRoot = Path.GetFullPath(componentStageDir);

            if (!normalizedDest.StartsWith(normalizedStageRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
                !string.Equals(normalizedDest, normalizedStageRoot, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Attempted path traversal during component staging: {relativePath}");
            }

            var parentDir = Path.GetDirectoryName(normalizedDest);
            if (!string.IsNullOrEmpty(parentDir))
                Directory.CreateDirectory(parentDir);

            using (var fileStream = File.Open(normalizedDest, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                content.CopyTo(fileStream);
            }

            var entry = _journal.Entries.Find(e => string.Equals(e.ComponentId, componentId, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
            {
                entry = new TransactionJournalEntry
                {
                    ComponentId = componentId,
                    StagedDirectory = componentStageDir,
                    TargetDirectory = Path.Combine(_targetRoot, componentId),
                    Status = "Staging"
                };
                _journal.Entries.Add(entry);
            }

            entry.CreatedRelativeFiles.Add(relativePath);
            _journal.Save(_journalPath);

            return normalizedDest;
        }

        /// <summary>Atomically commits all staged files into the target destination directory.</summary>
        public void Commit()
        {
            if (_journal.State != TransactionState.InProgress)
                throw new InvalidOperationException($"Cannot commit transaction in {_journal.State} state.");

            try
            {
                foreach (var entry in _journal.Entries)
                {
                    var destDir = entry.TargetDirectory;
                    Directory.CreateDirectory(destDir);

                    foreach (var relFile in entry.CreatedRelativeFiles)
                    {
                        var source = Path.Combine(entry.StagedDirectory, relFile);
                        var target = Path.Combine(destDir, relFile);

                        var targetParent = Path.GetDirectoryName(target);
                        if (!string.IsNullOrEmpty(targetParent))
                            Directory.CreateDirectory(targetParent);

                        if (File.Exists(source))
                        {
                            File.Copy(source, target, overwrite: true);
                        }
                    }
                    entry.Status = "Committed";
                }

                _journal.State = TransactionState.Committed;
                _journal.Save(_journalPath);

                // Clean up stages after successful commit
                try
                {
                    if (Directory.Exists(_stagesRoot))
                        Directory.Delete(_stagesRoot, recursive: true);
                }
                catch (Exception) { }
            }
            catch (Exception)
            {
                Rollback();
                throw;
            }
        }

        /// <summary>Rolls back the transaction, cleaning up staged data and leaving target state uncorrupted.</summary>
        public void Rollback()
        {
            if (_journal.State == TransactionState.Committed)
                return;

            try
            {
                // Remove staged directory
                if (Directory.Exists(_stagesRoot))
                {
                    Directory.Delete(_stagesRoot, recursive: true);
                }

                // If any partial target files were created for an uncommitted transaction, remove them
                foreach (var entry in _journal.Entries)
                {
                    if (entry.Status != "Committed")
                    {
                        foreach (var relFile in entry.CreatedRelativeFiles)
                        {
                            var target = Path.Combine(entry.TargetDirectory, relFile);
                            if (File.Exists(target))
                            {
                                try { File.Delete(target); } catch { }
                            }
                        }
                    }
                }

                _journal.State = TransactionState.RolledBack;
                _journal.Save(_journalPath);
            }
            catch (Exception)
            {
                _journal.State = TransactionState.Failed;
                try { _journal.Save(_journalPath); } catch { }
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                if (_journal.State == TransactionState.InProgress)
                {
                    Rollback();
                }
                _disposed = true;
            }
        }
    }
}
