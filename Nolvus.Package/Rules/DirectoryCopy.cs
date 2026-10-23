using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using Nolvus.Core.Services;
using Nolvus.Package.Utilities;

namespace Nolvus.Package.Rules
{
    public class DirectoryCopy : CopyRule
    {
        public bool IncludeRootDirectory { get; set; }

        public override void Load(XmlNode node)
        {
            base.Load(node);
            IncludeRootDirectory = Convert.ToBoolean(node["IncludeRootDirectory"]?.InnerText ?? "false");

            // Normalize inherited CopyRule.Source and CopyRule.DestinationDirectory
            Source = NormalizePath(Source);
            DestinationDirectory = NormalizePath(DestinationDirectory);
        }

        public override void Execute(string gamePath, string extractDir, string modDir, string instanceDir)
        {
            if (!CanExecute(gamePath, modDir))
                return;

            // Determine base destination
            string destBase =
                Destination == 0 ? modDir :
                Destination == 1 ? gamePath :
                instanceDir;

            //Normalize Rule Paths
            string normalizedSource = NormalizePath(Source);
            string normalizedDest = NormalizePath(DestinationDirectory);

            string? resolvedSource;
            if (string.IsNullOrWhiteSpace(normalizedSource))
            {
                resolvedSource = extractDir;
            }
            else if (
                !PathResolver.TryResolvePathUnderRoot(
                    extractDir,
                    normalizedSource,
                    out string normalizedSourceFull
                )
            )
            {
                ServiceSingleton.Logger?.Log(
                    $"DirectoryCopy skipped source outside extraction root: {Source}"
                );
                return;
            }
            else if (
                !PathResolver.TryResolvePathUnderRoot(extractDir, ".", out string resolvedExtractionRoot)
            )
            {
                ServiceSingleton.Logger?.Log("DirectoryCopy skipped because its extraction root is invalid.");
                return;
            }
            else
            {
                string normalizedSourceRelative = Path.GetRelativePath(
                    resolvedExtractionRoot,
                    normalizedSourceFull
                );

                // Resolve segment-by-segment to preserve case-insensitive archive matching.
                resolvedSource = normalizedSourceRelative == "."
                    ? extractDir
                    : PathResolver.ResolvePathSegments(extractDir, normalizedSourceRelative);
            }

            // ------------------------------------------------------------
            // 2) Fallback for FLAT archives (files directly at extract root)
            // ------------------------------------------------------------
            if (resolvedSource == null)
            {
                bool extractHasRootFiles = Directory.GetFiles(extractDir).Any();

                if (extractHasRootFiles)
                {
                    ServiceSingleton.Logger.Log(
                        $"DirectoryCopy fallback: using extract root '{extractDir}' because '{Source}' not found.");
                    resolvedSource = extractDir;
                }
            }

            // ------------------------------------------------------------
            // 3) If still null → path truly not found
            // ------------------------------------------------------------
            if (resolvedSource == null)
            {
                ServiceSingleton.Logger.Log($"DirectoryCopy skipped: source not found: {Source}");
                return;
            }

            string sourceAbsolute = resolvedSource;

            // ------------------------------------------------------------
            // 4) Determine final destination path
            // ------------------------------------------------------------
            string finalDestination;
            if (CopyToRoot)
            {
                if (!PathResolver.TryResolvePathUnderRoot(destBase, ".", out finalDestination))
                    return;
            }
            else if (!PathResolver.TryResolvePathUnderRoot(destBase, normalizedDest, out finalDestination))
            {
                ServiceSingleton.Logger?.Log(
                    $"DirectoryCopy skipped destination outside its root: {DestinationDirectory}"
                );
                return;
            }

            Directory.CreateDirectory(finalDestination);

            // ------------------------------------------------------------
            // 5) Copy directories
            // ------------------------------------------------------------
            if (!IncludeRootDirectory)
            {
                // Only contents
                CopyDirectoryContents(sourceAbsolute, finalDestination);
            }
            else
            {
                // Include root folder name
                string rootName = new DirectoryInfo(sourceAbsolute).Name;
                string destWithRoot = Path.Combine(finalDestination, rootName);

                Directory.CreateDirectory(destWithRoot);
                CopyDirectoryContents(sourceAbsolute, destWithRoot);
            }
        }


        private void CopyDirectoryContents(string src, string dst)
        {
            // Copy files
            foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            {
                string localPath = Path.GetRelativePath(src, file);
                localPath = NormalizePath(localPath);

                if (!PathResolver.TryResolvePathUnderRoot(src, localPath, out string sourcePath))
                {
                    ServiceSingleton.Logger?.Log(
                        $"DirectoryCopy skipped source file outside its root: {localPath}"
                    );
                    continue;
                }

                if (!PathResolver.TryResolvePathUnderRoot(dst, localPath, out string targetPath))
                {
                    ServiceSingleton.Logger?.Log(
                        $"DirectoryCopy skipped destination file outside its root: {localPath}"
                    );
                    continue;
                }

                string? targetDirectory = Path.GetDirectoryName(targetPath);
                if (string.IsNullOrWhiteSpace(targetDirectory))
                    continue;

                Directory.CreateDirectory(targetDirectory);
                File.Copy(sourcePath, targetPath, true);
            }
        }

        private string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            path = path.Replace("\\", "/");

            // Prevent absolute paths on Linux
            while (path.StartsWith("/"))
                path = path.TrimStart('/');

            return path;
        }
    }
}
