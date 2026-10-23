using System;
using System.IO;
using System.Xml;
using Nolvus.Package.Utilities;

namespace Nolvus.Package.Conditions
{
    /// <summary>
    /// Compares file existence with ValueToCompare. Operator 0 means equal and 1 means not equal.
    /// </summary>
    public class FileExistsCondition : CompareCondition
    {
        /// <summary>
        /// Selects the lookup root: 0 for the install directory, 1 for the game directory.
        /// </summary>
        public int Source { get; set; }

        public override void Load(XmlNode Node)
        {
            DataToCompare = Node["DataToCompare"]?.InnerText ?? string.Empty;
            ValueToCompare = Node["ValueToCompare"]?.InnerText ?? string.Empty;

            if (!int.TryParse(Node["Operator"]?.InnerText, out int OperatorValue))
                Operator = -1;
            else
                Operator = OperatorValue;

            XmlNode SourceNode = Node["Source"];
            if (SourceNode == null)
            {
                Source = 0;
                return;
            }

            Source = int.TryParse(SourceNode.InnerText, out int SourceValue) ? SourceValue : -1;
        }

        public override bool IsValid(string GamePath, string InstallDir)
        {
            if (Source is < 0 or > 1 || !bool.TryParse(ValueToCompare, out bool ExpectedExists))
                return false;

            string Root = Source == 1 ? GamePath : InstallDir;

            if (!PathResolver.TryResolvePathUnderRoot(Root, DataToCompare, out string FilePath))
                return false;

            bool Exists = File.Exists(FilePath);

            return Operator switch
            {
                0 => Exists == ExpectedExists,
                1 => Exists != ExpectedExists,
                _ => false
            };
        }
    }
}
