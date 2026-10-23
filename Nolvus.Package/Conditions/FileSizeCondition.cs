using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading.Tasks;
using System.Xml;
using Nolvus.Package.Utilities;

namespace Nolvus.Package.Conditions
{
    public class FileSizeCondition : CompareCondition
    {
        public int Source { get; set; }

        public override void Load(XmlNode Node)
        {
            base.Load(Node);
            Source = System.Convert.ToInt16(Node["Source"].InnerText);
        }

        public override bool IsValid(string GamePath, string InstallDir)
        {
            bool Valid = false;

            string SourceDir = string.Empty;

            if (Source == 1)
            {
                SourceDir = GamePath;
            }
            else
            {
                SourceDir = InstallDir;
            }

            if (!PathResolver.TryResolvePathUnderRoot(SourceDir, DataToCompare, out string filePath))
                return false;

            if (!File.Exists(filePath))
                return false;

            if (!int.TryParse(ValueToCompare, out int expectedSize))
                return false;

            FileInfo fileInfo = new FileInfo(filePath);

            long Size = fileInfo.Length / 1024;

            switch (this.Operator)
            {
                case 0:
                    Valid = Size == expectedSize;
                    break;
                case 1:
                    Valid = Size != expectedSize;
                    break;
                case 2:
                    Valid = Size > expectedSize;
                    break;
                case 3:
                    Valid = Size < expectedSize;
                    break;

            }

            return Valid;
        }
    }
}
