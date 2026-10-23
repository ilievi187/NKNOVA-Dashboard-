using System.Xml;
using Nolvus.Core.Interfaces;
using Nolvus.Core.Services;
using Nolvus.Package.Rules;
using Xunit;

namespace Nolvus.Package.Tests.Rules;

public sealed class DirectoryCopyRuleTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"nolvus-directory-copy-tests-{Guid.NewGuid():N}"
    );

    public DirectoryCopyRuleTests()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        ServiceSingleton.RegisterService<ILogService>(new TestLogService());
    }

    [Fact]
    public void Destination_directory_traversal_does_not_copy_outside_mod_directory()
    {
        string extractionDirectory = Path.Combine(_temporaryDirectory, "extract");
        string sourceDirectory = Path.Combine(extractionDirectory, "source");
        string modDirectory = Path.Combine(_temporaryDirectory, "mods");
        string escapedDirectory = Path.Combine(_temporaryDirectory, "outside");

        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(modDirectory);
        File.WriteAllText(Path.Combine(sourceDirectory, "payload.dat"), "payload");

        var document = new XmlDocument();
        document.LoadXml(
            """
            <Rule>
              <Source>source</Source>
              <Destination>0</Destination>
              <DestinationDirectory>../outside</DestinationDirectory>
              <IncludeRootDirectory>false</IncludeRootDirectory>
            </Rule>
            """
        );

        var rule = new DirectoryCopy();
        rule.Load(document.DocumentElement!);
        rule.Execute(
            Path.Combine(_temporaryDirectory, "game"),
            extractionDirectory,
            modDirectory,
            Path.Combine(_temporaryDirectory, "instance")
        );

        Assert.False(File.Exists(Path.Combine(escapedDirectory, "payload.dat")));
    }

    [Fact]
    public void Relative_destination_copies_directory_contents_inside_mod_directory()
    {
        string extractionDirectory = Path.Combine(_temporaryDirectory, "extract");
        string sourceDirectory = Path.Combine(extractionDirectory, "source");
        string modDirectory = Path.Combine(_temporaryDirectory, "mods");
        string destinationFile = Path.Combine(modDirectory, "meshes", "payload.dat");

        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(modDirectory);
        File.WriteAllText(Path.Combine(sourceDirectory, "payload.dat"), "payload");

        var document = new XmlDocument();
        document.LoadXml(
            """
            <Rule>
              <Source>source</Source>
              <Destination>0</Destination>
              <DestinationDirectory>meshes</DestinationDirectory>
              <IncludeRootDirectory>false</IncludeRootDirectory>
            </Rule>
            """
        );

        var rule = new DirectoryCopy();
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
    public void Parent_traversal_source_does_not_trigger_flat_archive_fallback()
    {
        string extractionDirectory = Path.Combine(_temporaryDirectory, "extract");
        string modDirectory = Path.Combine(_temporaryDirectory, "mods");
        string destinationDirectory = Path.Combine(modDirectory, "safe");

        Directory.CreateDirectory(extractionDirectory);
        Directory.CreateDirectory(modDirectory);
        File.WriteAllText(Path.Combine(extractionDirectory, "root.dat"), "root payload");

        var document = new XmlDocument();
        document.LoadXml(
            """
            <Rule>
              <Source>../outside</Source>
              <Destination>0</Destination>
              <DestinationDirectory>safe</DestinationDirectory>
              <IncludeRootDirectory>false</IncludeRootDirectory>
            </Rule>
            """
        );

        var rule = new DirectoryCopy();
        rule.Load(document.DocumentElement!);
        rule.Execute(
            Path.Combine(_temporaryDirectory, "game"),
            extractionDirectory,
            modDirectory,
            Path.Combine(_temporaryDirectory, "instance")
        );

        Assert.False(File.Exists(Path.Combine(destinationDirectory, "root.dat")));
    }

    [Fact]
    public void In_root_parent_segment_resolves_without_using_flat_archive_fallback()
    {
        string extractionDirectory = Path.Combine(_temporaryDirectory, "extract");
        string nestedDirectory = Path.Combine(extractionDirectory, "nested");
        string sourceDirectory = Path.Combine(extractionDirectory, "source");
        string modDirectory = Path.Combine(_temporaryDirectory, "mods");
        string destinationFile = Path.Combine(modDirectory, "safe", "payload.dat");

        Directory.CreateDirectory(nestedDirectory);
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(modDirectory);
        File.WriteAllText(Path.Combine(sourceDirectory, "payload.dat"), "payload");

        var document = new XmlDocument();
        document.LoadXml(
            """
            <Rule>
              <Source>nested/../source</Source>
              <Destination>0</Destination>
              <DestinationDirectory>safe</DestinationDirectory>
              <IncludeRootDirectory>false</IncludeRootDirectory>
            </Rule>
            """
        );

        var rule = new DirectoryCopy();
        rule.Load(document.DocumentElement!);
        rule.Execute(
            Path.Combine(_temporaryDirectory, "game"),
            extractionDirectory,
            modDirectory,
            Path.Combine(_temporaryDirectory, "instance")
        );

        Assert.Equal("payload", File.ReadAllText(destinationFile));
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
