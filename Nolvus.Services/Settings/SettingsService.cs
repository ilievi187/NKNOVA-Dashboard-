using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using IniParser;
using IniParser.Model;
using Nolvus.Core.Interfaces;
using System.ComponentModel;

namespace Nolvus.Services.Settings
{
    public class SettingsService : ISettingsService
    {
        private static readonly object AtomicWriteLock = new();

        public const string IniFile = "NolvusDashboard.ini";
        public const string ProcessSection = "Process";
        public const string Count = "Count";
        public const string Retry = "Retry";
        public const string EThreshold = "ErrorsThreshold";
        public const string MiscSection = "Misc";
        public const string ForceAntiAliasing = "ForceAA";
        public const string Interval = "RefreshInterval";
        public const string Browser = "Browser";
        public const string LogSeverity = "LogSeverity";
        public const string AutoClick = "AutoClick";
        public const string DevSection = "Dev";
        public const string DebugEnabled = "Debug";

        public void StoreIniValue(string Section, string Parameter, string Value)
        {
            UpdateIniFileAtomically(IniFile, data => data[Section][Parameter] = Value);
        }

        public static void UpdateIniFileAtomically(string filePath, Action<IniData> update)
        {
            ArgumentNullException.ThrowIfNull(update);
            lock (AtomicWriteLock)
            {
                var Parser = new FileIniDataParser();

                IniData Data = Parser.ReadFile(filePath);
                update(Data);

                WriteTextAtomicallyCore(filePath, SerializeIniData(Data));
            }
        }

        private static string SerializeIniData(IniData data)
        {
            using var memory = new MemoryStream();
            using (var writer = new StreamWriter(memory, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 1024, leaveOpen: true))
            {
                var streamParser = new StreamIniDataParser(new IniParser.Parser.IniDataParser());
                streamParser.WriteData(writer, data);
                writer.Flush();
            }

            return Encoding.UTF8.GetString(memory.ToArray());
        }

        public void StoreIniValue(string File, string Section, string Parameter, string Value)
        {
            var Parser = new FileIniDataParser();

            IniData Data = Parser.ReadFile(File);

            Data[Section][Parameter] = Value;

            Parser.WriteFile(File, Data);
        }

        /// <summary>
        /// Replaces a settings file only after its complete contents are flushed to a sibling temporary file.
        /// On Unix the temporary file is owner-readable and owner-writable only.
        /// </summary>
        public static void WriteTextAtomically(string filePath, string contents)
        {
            lock (AtomicWriteLock)
                WriteTextAtomicallyCore(filePath, contents);
        }

        private static void WriteTextAtomicallyCore(string filePath, string contents)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("A settings file path is required.", nameof(filePath));
            ArgumentNullException.ThrowIfNull(contents);

            string fullPath = Path.GetFullPath(filePath);
            string directory = Path.GetDirectoryName(fullPath)
                ?? throw new IOException("Settings file path has no parent directory.");
            if (!Directory.Exists(directory))
                throw new DirectoryNotFoundException("Settings directory does not exist; existing settings were not changed.");

            string temporaryPath = Path.Combine(
                directory,
                $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
            try
            {
                using (var stream = new FileStream(
                           temporaryPath,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None,
                           4096,
                           FileOptions.WriteThrough))
                {
                    if (!OperatingSystem.IsWindows())
                        File.SetUnixFileMode(temporaryPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);

                    using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 1024, leaveOpen: true))
                    {
                        writer.Write(contents);
                        writer.Flush();
                    }

                    stream.Flush(flushToDisk: true);
                }

                var destination = new FileInfo(fullPath);
                if (destination.LinkTarget is not null)
                    throw new IOException("Settings destination must not be a symbolic link or reparse point.");

                File.Move(temporaryPath, fullPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }

        public string GetIniValue(string Section, string Parameter)
        {
            var Parser = new FileIniDataParser();

            IniData Data = Parser.ReadFile(IniFile);

            return Data[Section][Parameter];
        }

        public string GetIniValue(string File, string Section, string Parameter)
        {
            var Parser = new FileIniDataParser();

            IniData Data = Parser.ReadFile(File);

            return Data[Section][Parameter];
        }

        public FileIniDataParser GetIniParser()
        {
            return new FileIniDataParser();
        }

        public bool IniKeyExists(string Section, string Key)
        {
            var Parser = new FileIniDataParser();

            IniData Data = Parser.ReadFile(IniFile);

            var _Section = Data.Sections.Where(x => x.SectionName == Section).FirstOrDefault();

            if (_Section != null)
            {
                return _Section.Keys.Where(x => x.KeyName == Key).FirstOrDefault() != null;
            }
            else
            {
                return false;
            }
        }

        public int ProcessCount
        {
            get
            {
                try
                {
                    var p = System.Convert.ToInt16(GetIniValue(ProcessSection, Count));
                    return p == 0 ? Environment.ProcessorCount : p;
                }
                catch
                {
                    return Environment.ProcessorCount;
                }
            }
            
            set
            {
                StoreIniValue(ProcessSection, Count, value.ToString());
            }
        }

        public int RetryCount
        {
            get
            {
                try
                {
                    var r = System.Convert.ToInt16(GetIniValue(ProcessSection, Retry));
                    return r == 0 ? 3 : r;
                }
                catch
                {
                    return 3;
                }
            }
        }

        public bool ForceAA
        {
            get
            {
                try
                {
                    return System.Convert.ToBoolean(GetIniValue(MiscSection, ForceAntiAliasing));
                    
                }
                catch
                {
                    return false;
                }
            }
        }

        public int RefreshInterval
        {
            get
            {
                try
                {
                    var r = System.Convert.ToInt16(GetIniValue(MiscSection, Interval));
                    return r == 0 || r > 1000 ? 10 : r;
                }
                catch
                {
                    return 10;
                }
            }
        }

        public int ErrorsThreshold
        {
            get
            {
                try
                {
                    var r = System.Convert.ToInt16(GetIniValue(ProcessSection, EThreshold));
                    return r > 100 ? 50 : r;
                }
                catch
                {
                    return 50;
                }
            }
        }

        public string BrowserLogSeverity
        {
            get
            {
                try
                {
                    var l = GetIniValue(Browser, LogSeverity);

                    return l == null ? "Error" : l;
                }
                catch
                {
                    return "Error";
                }
            }
        }

        public bool DevDebug
        {
            get
            {
                try
                {
                    return System.Convert.ToBoolean(GetIniValue(DevSection, DebugEnabled));
                }
                catch
                {
                    return false;
                }
            }
        }

        public bool NexusAutoClick
        {
            get
            {
                try
                {
                    return System.Convert.ToBoolean(GetIniValue(Browser, AutoClick));
                }
                catch
                {
                    return true;
                }
            }
            set
            {
                StoreIniValue(Browser, AutoClick, value.ToString());
            }
        }
    }
}
