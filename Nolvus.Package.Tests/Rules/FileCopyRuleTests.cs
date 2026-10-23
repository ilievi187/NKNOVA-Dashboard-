using System.Xml;
using Nolvus.Core.Interfaces;
using Nolvus.Core.Services;
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
        ServiceSingleton.RegisterService<ILogService>(new TestLogService());
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

    [Fact]
    public void Destination_directory_traversal_does_not_write_outside_mod_directory()
    {
        string extractionDirectory = Path.Combine(_temporaryDirectory, "extract");
        string modDirectory = Path.Combine(_temporaryDirectory, "mods");
        string escapedDirectory = Path.Combine(_temporaryDirectory, "outside");

        Directory.CreateDirectory(extractionDirectory);
        Directory.CreateDirectory(modDirectory);
        File.WriteAllText(Path.Combine(extractionDirectory, "source.dat"), "payload");

        var document = new XmlDocument();
        document.LoadXml(
            """
            <Rule>
              <Source>source.dat</Source>
              <Destination>0</Destination>
              <DestinationDirectory>../outside</DestinationDirectory>
            </Rule>
            """
        );

        var rule = new FileCopy();
        rule.Load(document.DocumentElement!);
        rule.Execute(
            Path.Combine(_temporaryDirectory, "game"),
            extractionDirectory,
            modDirectory,
            Path.Combine(_temporaryDirectory, "instance")
        );

        Assert.False(File.Exists(Path.Combine(escapedDirectory, "source.dat")));
    }

    [Fact]
    public void Relative_destination_and_new_file_name_copy_inside_mod_directory()
    {
        string extractionDirectory = Path.Combine(_temporaryDirectory, "extract");
        string modDirectory = Path.Combine(_temporaryDirectory, "mods");
        string destinationFile = Path.Combine(modDirectory, "plugins", "actors", "renamed.dat");

        Directory.CreateDirectory(extractionDirectory);
        Directory.CreateDirectory(modDirectory);
        File.WriteAllText(Path.Combine(extractionDirectory, "source.dat"), "payload");

        var document = new XmlDocument();
        document.LoadXml(
            """
            <Rule>
              <Source>source.dat</Source>
              <Destination>0</Destination>
              <DestinationDirectory>plugins/actors</DestinationDirectory>
              <NewFileName>renamed.dat</NewFileName>
            </Rule>
            """
        );

        var rule = new FileCopy();
        rule.Load(document.DocumentElement!);
        rule.Execute(
            Path.Combine(_temporaryDirectory, "game"),
            extractionDirectory,
            modDirectory,
            Path.Combine(_temporaryDirectory, "instance")
        );

        Assert.Equal("payload", File.ReadAllText(destinationFile));
    }

    [Fact]
    public void Source_traversal_does_not_read_outside_extraction_directory()
    {
        string extractionDirectory = Path.Combine(_temporaryDirectory, "extract");
        string modDirectory = Path.Combine(_temporaryDirectory, "mods");
        Directory.CreateDirectory(extractionDirectory);
        Directory.CreateDirectory(modDirectory);

        string outsideFile = Path.Combine(_temporaryDirectory, "outside.dat");
        File.WriteAllText(outsideFile, "outside payload");

        var document = new XmlDocument();
        document.LoadXml(
            """
            <Rule>
              <Source>../outside.dat</Source>
              <Destination>0</Destination>
              <DestinationDirectory></DestinationDirectory>
            </Rule>
            """
        );

        var rule = new FileCopy();
        rule.Load(document.DocumentElement!);
        rule.Execute(
            Path.Combine(_temporaryDirectory, "game"),
            extractionDirectory,
            modDirectory,
            Path.Combine(_temporaryDirectory, "instance")
        );

        Assert.False(File.Exists(Path.Combine(modDirectory, "outside.dat")));
    }

    [Fact]
    public void New_file_name_traversal_does_not_write_outside_target_directory()
    {
        string extractionDirectory = Path.Combine(_temporaryDirectory, "extract");
        string modDirectory = Path.Combine(_temporaryDirectory, "mods");
        Directory.CreateDirectory(extractionDirectory);
        Directory.CreateDirectory(modDirectory);
        File.WriteAllText(Path.Combine(extractionDirectory, "source.dat"), "payload");

        var document = new XmlDocument();
        document.LoadXml(
            """
            <Rule>
              <Source>source.dat</Source>
              <Destination>0</Destination>
              <DestinationDirectory></DestinationDirectory>
              <NewFileName>../outside.dat</NewFileName>
            </Rule>
            """
        );

        var rule = new FileCopy();
        rule.Load(document.DocumentElement!);
        rule.Execute(
            Path.Combine(_temporaryDirectory, "game"),
            extractionDirectory,
            modDirectory,
            Path.Combine(_temporaryDirectory, "instance")
        );

        Assert.False(File.Exists(Path.Combine(_temporaryDirectory, "outside.dat")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
            Directory.Delete(_temporaryDirectory, recursive: true);
    }

    private sealed class TestLogService : ILogService
    {
        public void Log(string message) { }

        public void LineBreak() { }
    }
}
