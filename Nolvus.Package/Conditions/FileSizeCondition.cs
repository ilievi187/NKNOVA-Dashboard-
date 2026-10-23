using System.IO;
using System.Xml;
using Nolvus.Package.Utilities;

namespace Nolvus.Package.Conditions
{
    public class FileSizeCondition : CompareCondition
    {
        public int Source { get; set; }

        public override void Load(XmlNode Node)
        {
            DataToCompare = Node["DataToCompare"]?.InnerText ?? string.Empty;
            ValueToCompare = Node["ValueToCompare"]?.InnerText ?? string.Empty;
            Operator = int.TryParse(Node["Operator"]?.InnerText, out int OperatorValue)
                ? OperatorValue
                : -1;

            XmlNode SourceNode = Node["Source"];
            Source = SourceNode != null && int.TryParse(SourceNode.InnerText, out int SourceValue)
                ? SourceValue
                : -1;
        }

        public override bool IsValid(string GamePath, string InstallDir)
        {
            if (Source is < 0 or > 1 || Operator is < 0 or > 3)
                return false;

            string SourceDir = Source == 1 ? GamePath : InstallDir;

            if (!PathResolver.TryResolvePathUnderRoot(SourceDir, DataToCompare, out string filePath))
                return false;

            if (!File.Exists(filePath))
                return false;

            if (!int.TryParse(ValueToCompare, out int expectedSize))
                return false;

            if (expectedSize < 0)
                return false;

            FileInfo fileInfo = new FileInfo(filePath);

            long Size = fileInfo.Length / 1024;

            return Operator switch
            {
                0 => Size == expectedSize,
                1 => Size != expectedSize,
                2 => Size > expectedSize,
                3 => Size < expectedSize,
                _ => false
            };
        }
    }
}
