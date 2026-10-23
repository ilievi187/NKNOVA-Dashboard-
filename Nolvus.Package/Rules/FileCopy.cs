using System;
using System.IO;
using System.Xml;
using Nolvus.Core.Services;
using Nolvus.Package.Utilities;

namespace Nolvus.Package.Rules
{
    public class FileCopy : CopyRule
    {
        public string NewFileName { get; set; }

        public override void Load(XmlNode node)
        {
            base.Load(node);

            // Normalize all inherited paths
            Source = Normalize(Source);
            DestinationDirectory = Normalize(DestinationDirectory);

            NewFileName = Normalize(node["NewFileName"]?.InnerText ?? string.Empty);
        }

        public override void Execute(string gamePath, string extractDir, string modDir, string instanceDir)
        {
            if (!CanExecute(gamePath, modDir))
                return;

            ServiceSingleton.Logger.Log(
                $"[FileCopy] Source='{Source}' DestDir='{DestinationDirectory}' NormalizedSource='{Normalize(Source)}' NormalizedDest='{Normalize(DestinationDirectory)}'"
            );

            // Always normalize before using
            string srcRel = Normalize(Source);
            string destRel = Normalize(DestinationDirectory);

            // Pick destination base
            string destBase =
                Destination == 0 ? modDir :
                Destination == 1 ? gamePath :
                instanceDir;

            if (!PathResolver.TryResolvePathUnderRoot(extractDir, srcRel, out string sourceFull))
            {
                ServiceSingleton.Logger.Log($"[FileCopy] Skipping source outside extraction root: {Source}");
                return;
            }

            if (!File.Exists(sourceFull))
            {
                ServiceSingleton.Logger.Log($"[FileCopy] Skipping missing file: {sourceFull}");
                return;
            }

            string targetDir;
            if (CopyToRoot)
            {
                if (!PathResolver.TryResolvePathUnderRoot(destBase, ".", out targetDir))
                    return;
            }
            else if (!PathResolver.TryResolvePathUnderRoot(destBase, destRel, out targetDir))
            {
                ServiceSingleton.Logger.Log(
                    $"[FileCopy] Skipping destination outside its root: {DestinationDirectory}"
                );
                return;
            }

            // Determine actual directory to copy into
            Directory.CreateDirectory(targetDir);

            // Determine final filename
            string finalName = !string.IsNullOrWhiteSpace(NewFileName)
                ? Normalize(NewFileName)
                : Path.GetFileName(sourceFull);

            if (!PathResolver.TryResolvePathUnderRoot(targetDir, finalName, out string destFull))
            {
                ServiceSingleton.Logger.Log($"[FileCopy] Skipping destination file outside its root: {NewFileName}");
                return;
            }

            string? finalDirectory = Path.GetDirectoryName(destFull);
            if (string.IsNullOrWhiteSpace(finalDirectory))
                return;

            Directory.CreateDirectory(finalDirectory);

            // Perform the copy
            File.Copy(sourceFull, destFull, overwrite: true);

            ServiceSingleton.Logger.Log($"[FileCopy] Copied: {sourceFull} → {destFull}");
        }

        private string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            // Windows → Unix separators first
            path = path.Replace("\\", "/");

            // Prevent absolute paths
            while (path.StartsWith("/"))
                path = path.TrimStart('/');

            return path;
        }
    }
}
