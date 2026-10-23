using System.Xml;
using Nolvus.Core.Interfaces;
using Nolvus.Core.Services;
using Nolvus.Package.Rules;
using Xunit;

namespace Nolvus.Package.Tests.Rules;

public sealed class FileSizeConditionRuleTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"nolvus-file-size-rule-tests-{Guid.NewGuid():N}"
    );

    public FileSizeConditionRuleTests()
    {
        Directory.CreateDirectory(_root);
        ServiceSingleton.RegisterService<ILogService>(new TestLogService());
    }

    [Fact]
    public void Malformed_file_size_source_xml_fails_closed_in_rule_loader()
    {
        string gameDirectory = Path.Combine(_root, "game");
        string extractionDirectory = Path.Combine(_root, "extract");
        string modDirectory = Path.Combine(_root, "mods");
        string instanceDirectory = Path.Combine(_root, "instance");

        Directory.CreateDirectory(gameDirectory);
        Directory.CreateDirectory(extractionDirectory);
        Directory.CreateDirectory(modDirectory);
        Directory.CreateDirectory(instanceDirectory);
        File.WriteAllText(Path.Combine(extractionDirectory, "source.dat"), "payload");
        File.WriteAllBytes(Path.Combine(modDirectory, "ready.flag"), new byte[1024]);

        var document = new XmlDocument();
        document.LoadXml(
            """
            <Rule>
              <Source>source.dat</Source>
              <Destination>0</Destination>
              <DestinationDirectory></DestinationDirectory>
              <Conditions>
                <Condition>
                  <Type>FileSizeCondition</Type>
                  <DataToCompare>ready.flag</DataToCompare>
                  <Operator>0</Operator>
                  <ValueToCompare>1</ValueToCompare>
                  <Source>not-a-source</Source>
                </Condition>
              </Conditions>
            </Rule>
            """
        );

        var rule = new FileCopy();
        rule.Load(document.DocumentElement!);
        rule.Execute(gameDirectory, extractionDirectory, modDirectory, instanceDirectory);

        Assert.False(File.Exists(Path.Combine(modDirectory, "source.dat")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed class TestLogService : ILogService
    {
        public void Log(string message) { }
        public void LineBreak() { }
    }
}
