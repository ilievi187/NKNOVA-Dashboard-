using System;
using System.IO;
using System.Linq;

namespace Nolvus.Package.Utilities
{
    public static class PathResolver
    {
        /// <summary>
        /// Resolves a multi-segment directory path inside <paramref name="root"/>
        /// in a case-insensitive way. Returns the real existing directory path
        /// or null if not found.
        /// </summary>
        public static string? ResolveCaseInsensitiveDirectory(string root, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(relativePath))
                return null;

            string current = root;

            foreach (var segment in relativePath.Split(new[] { '/', '\\' },
                            StringSplitOptions.RemoveEmptyEntries))
            {
                if (!Directory.Exists(current))
                    return null;

                var match = FindCaseInsensitiveMatch(
                    Directory.GetDirectories(current),
                    segment
                );

                if (match == null)
                    return null;

                current = match;
            }

            return current;
        }

        /// <summary>
        /// Resolves a file name inside a directory in a case-insensitive way.
        /// Returns full absolute file path or null.
        /// </summary>
        public static string? ResolveCaseInsensitiveFile(string directory, string fileName)
        {
            if (!Directory.Exists(directory))
                return null;

            return FindCaseInsensitiveMatch(
                Directory.GetFiles(directory), 
                fileName
            );
        }

        /// <summary>
        /// Finds a directory or file in <paramref name="entries"/> matching <paramref name="name"/>
        /// using case-insensitive comparison. Returns absolute path or null.
        /// </summary>
        private static string? FindCaseInsensitiveMatch(string[] entries, string name)
        {
            // first try exact match
            var exact = entries
                .FirstOrDefault(e => Path.GetFileName(e) == name);

            if (exact != null)
                return exact;

            // then case-insensitive
            return entries
                .FirstOrDefault(e => 
                    string.Equals(Path.GetFileName(e), name, StringComparison.OrdinalIgnoreCase));
        }


        /// <summary>
        ///  Finds the full absolute path of a file/folder in a directory and resolve Directory override in xml
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static string? ResolveExistingDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            path = path.Replace("\\", "/");
            string[] parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

            string current = Path.GetPathRoot(path) ?? "/";

            // If no root (Linux relative path)
            if (string.IsNullOrEmpty(current) || current == "")
                current = "/";

            foreach (var part in parts)
            {
                var resolved = ResolveCaseInsensitiveDirectory(current, part);
                if (resolved == null)
                    return null;

                current = resolved;
            }

            return current;
        }

            /// <summary>
            /// 
            /// </summary>
            /// <param name="baseDir"></param>
            /// <param name="relativePath"></param>
            /// <returns></returns>
            public static string? ResolvePathSegments(string baseDir, string relativePath)
            {
                if (string.IsNullOrWhiteSpace(relativePath))
                    return baseDir;

                string current = baseDir;
                var parts = relativePath.Replace("\\", "/").Split('/', StringSplitOptions.RemoveEmptyEntries);

                foreach (var part in parts)
                {
                    string? resolved = ResolveCaseInsensitiveDirectory(current, part);

                    if (resolved == null)
                        return null;

                    current = resolved;
                }

                return current;
            }

        /// <summary>
        /// Resolves a relative path under a root directory and rejects rooted paths,
        /// parent traversal, and symbolic links or junctions that resolve outside that root.
        /// </summary>
        public static bool TryResolvePathUnderRoot(
            string rootDirectory,
            string relativePath,
            out string resolvedPath
        )
        {
            resolvedPath = string.Empty;

            if (string.IsNullOrWhiteSpace(rootDirectory) || string.IsNullOrWhiteSpace(relativePath))
                return false;

            try
            {
                string fullRoot = Path.GetFullPath(rootDirectory);
                string normalizedPath = relativePath
                    .Replace('\\', Path.DirectorySeparatorChar)
                    .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

                if (Path.IsPathRooted(normalizedPath))
                    return false;

                if (!TryResolveExistingPathComponents(fullRoot, out string resolvedRoot))
                    return false;

                string currentPath = resolvedRoot;
                string[] segments = normalizedPath.Split(
                    new[] { Path.DirectorySeparatorChar },
                    StringSplitOptions.RemoveEmptyEntries
                );

                foreach (string segment in segments)
                {
                    if (segment == ".")
                        continue;

                    if (segment == "..")
                    {
                        string? parent = Path.GetDirectoryName(currentPath);
                        if (parent is null)
                            return false;

                        currentPath = parent;
                    }
                    else
                    {
                        string nextPath = Path.GetFullPath(Path.Combine(currentPath, segment));
                        if (!TryResolvePathLink(nextPath, out currentPath))
                            return false;
                    }

                    if (!IsPathWithinRoot(resolvedRoot, currentPath))
                        return false;
                }

                resolvedPath = Path.GetFullPath(currentPath);
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return false;
            }
        }

        private static bool TryResolveExistingPathComponents(string path, out string resolvedPath)
        {
            resolvedPath = string.Empty;
            string fullPath = Path.GetFullPath(path);
            string? pathRoot = Path.GetPathRoot(fullPath);
            if (string.IsNullOrEmpty(pathRoot))
                return false;

            string currentPath = pathRoot;
            string[] segments = fullPath[pathRoot.Length..].Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries
            );

            foreach (string segment in segments)
            {
                string nextPath = Path.GetFullPath(Path.Combine(currentPath, segment));
                if (!TryResolvePathLink(nextPath, out currentPath))
                    return false;
            }

            resolvedPath = Path.GetFullPath(currentPath);
            return true;
        }

        private static bool TryResolvePathLink(string path, out string resolvedPath)
        {
            resolvedPath = Path.GetFullPath(path);

            try
            {
                FileSystemInfo pathInfo = Directory.Exists(resolvedPath)
                    ? new DirectoryInfo(resolvedPath)
                    : new FileInfo(resolvedPath);

                if (pathInfo.LinkTarget is null)
                    return true;

                FileSystemInfo? linkTarget = pathInfo.ResolveLinkTarget(returnFinalTarget: true);
                if (linkTarget is null)
                    return false;

                // A link's target may itself contain a symlink in a parent component.
                // Walk the target path too instead of trusting only its lexical spelling.
                return TryResolveExistingPathComponents(linkTarget.FullName, out resolvedPath);
            }
            catch (FileNotFoundException)
            {
                // A non-existent path is valid as the destination of a future write.
                return true;
            }
            catch (DirectoryNotFoundException)
            {
                // A non-existent path is valid as the destination of a future write.
                return true;
            }
            catch (Exception ex) when (
                ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException
            )
            {
                return false;
            }
        }

        private static bool IsPathWithinRoot(string root, string path)
        {
            string relativePath = Path.GetRelativePath(root, path);
            StringComparison comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            return !Path.IsPathRooted(relativePath)
                && relativePath != ".."
                && !relativePath.StartsWith(".." + Path.DirectorySeparatorChar, comparison);
        }

    }
}
