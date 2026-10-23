using System.Xml;
using Nolvus.Package.Conditions;
using Nolvus.Package.Rules;
using Xunit;

namespace Nolvus.Package.Tests.Rules;

public sealed class FileCopyRuleTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"nolvus-rule-tests-{Guid.NewGuid():N}"
    );

    public FileCopyRuleTests()
    {
        Directory.CreateDirectory(_temporaryDirectory);
    }

    [Fact]
    public void Missing_file_size_condition_skips_copy_through_xml_rule_loader()
    {
        string gameDirectory = Path.Combine(_temporaryDirectory, "game");
        string extractionDirectory = Path.Combine(_temporaryDirectory, "extract");
        string modDirectory = Path.Combine(_temporaryDirectory, "mods");
        string instanceDirectory = Path.Combine(_temporaryDirectory, "instance");

        Directory.CreateDirectory(gameDirectory);
        Directory.CreateDirectory(extractionDirectory);
        Directory.CreateDirectory(modDirectory);
        Directory.CreateDirectory(instanceDirectory);
        File.WriteAllText(Path.Combine(extractionDirectory, "source.dat"), "payload");

        var document = new XmlDocument();
        document.LoadXml(
            """
            <Rule>
              <Source>source.dat</Source>
              <Destination>0</Destination>
              <DestinationDirectory></DestinationDirectory>
              <NewFileName></NewFileName>
              <Conditions>
                <Condition>
                  <Type>FileSizeCondition</Type>
                  <DataToCompare>missing.esp</DataToCompare>
                  <Operator>0</Operator>
                  <ValueToCompare>1</ValueToCompare>
                  <Source>0</Source>
                </Condition>
              </Conditions>
            </Rule>
            """
        );

        var rule = new FileCopy();
        rule.Load(document.DocumentElement!);

        Assert.IsType<FileSizeCondition>(Assert.Single(rule.Conditions));
        rule.Execute(gameDirectory, extractionDirectory, modDirectory, instanceDirectory);

        Assert.False(File.Exists(Path.Combine(modDirectory, "source.dat")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
            Directory.Delete(_temporaryDirectory, recursive: true);
    }
}
