using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Nolvus.Core.Utils
{
    public static class ExecutableResolver
    {
        public static string? FindExecutable(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            // If name is a path, do not search PATH. Windows paths use backslashes.
            if (Path.IsPathRooted(name)
                || name.Contains(Path.DirectorySeparatorChar)
                || name.Contains(Path.AltDirectorySeparatorChar))
            {
                foreach (var candidate in GetExecutableCandidates(Path.GetFullPath(name)))
                {
                    if (IsExecutable(candidate))
                        return candidate;
                }

                return null;
            }

            var pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrWhiteSpace(pathEnv))
                return null;

            foreach (var dir in pathEnv.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(dir))
                    continue;

                foreach (var candidate in GetExecutableCandidates(Path.Combine(dir.Trim(), name)))
                {
                    if (IsExecutable(candidate))
                        return candidate;
                }
            }

            return null;
        }

        public static string RequireExecutable(string name)
        {
            var path = FindExecutable(name);
            if (path == null)
                throw new FileNotFoundException($"Required executable '{name}' not found in PATH.");
            return path;
        }

        private static bool IsExecutable(string path)
        {
            if (!File.Exists(path))
                return false;

            // ProcessStartInfo with UseShellExecute=false launches Windows PE/COM
            // programs, not arbitrary extensionless files or command scripts.
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var extension = Path.GetExtension(path);
                return string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(extension, ".com", StringComparison.OrdinalIgnoreCase);
            }

            try
            {
                var mode = File.GetUnixFileMode(path);
                const UnixFileMode anyExec =
                    UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;

                return (mode & anyExec) != 0;
            }
            catch
            {
                // If we can't read mode assume not executable
                return false;
            }
        }

        private static string[] GetExecutableCandidates(string name)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && string.IsNullOrEmpty(Path.GetExtension(name)))
                return new[] { name + ".exe", name + ".com" };

            return new[] { name };
        }
    }
}
