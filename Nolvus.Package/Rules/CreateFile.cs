using System;
using System.IO;
using System.Xml;
using Nolvus.Package.Utilities;

namespace Nolvus.Package.Rules
{
    public class CreateFile : CreateRule
    {
        public string DefaultText { get; set; } = string.Empty;

        public override void Load(XmlNode Node)
        {
            base.Load(Node);
            DefaultText = Node["DefaultText"]?.InnerText ?? string.Empty;
        }

        public override void Execute(string GamePath, string ExtractDir, string ModDir, string InstanceDir)
        {
            if (Source is < 0 or > 1)
                return;

            if (!CanExecute(GamePath, ModDir))
                return;

            string baseDir = Source == 0 ? ModDir : GamePath;

            if (!PathResolver.TryResolvePathUnderRoot(baseDir, Name, out string fullPath))
                return;

            string fullRoot = Path.GetFullPath(baseDir);
            if (Path.GetRelativePath(fullRoot, fullPath) == ".")
                return;

            string? targetDirectory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrWhiteSpace(targetDirectory))
                return;

            Directory.CreateDirectory(targetDirectory);

            if (!string.IsNullOrEmpty(DefaultText))
            {
                File.WriteAllText(fullPath, DefaultText);
            }
            else
            {
                File.Create(fullPath).Dispose();
            }
        }
    }
}
