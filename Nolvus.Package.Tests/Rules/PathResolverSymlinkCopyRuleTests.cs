using System.Xml;
using Nolvus.Core.Interfaces;
using Nolvus.Core.Services;
using Nolvus.Package.Rules;
using Xunit;

namespace Nolvus.Package.Tests.Rules;

public sealed class PathResolverSymlinkCopyRuleTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"nolvus-copy-symlink-tests-{Guid.NewGuid():N}"
    );

    private string ExtractionDirectory => Path.Combine(_root, "extract");
    private string ModDirectory => Path.Combine(_root, "mods");
    private string ExternalDirectory => Path.Combine(_root, "external");

    public PathResolverSymlinkCopyRuleTests()
    {
        Directory.CreateDirectory(ExtractionDirectory);
        Directory.CreateDirectory(ModDirectory);
        Directory.CreateDirectory(ExternalDirectory);
        ServiceSingleton.RegisterService<ILogService>(new TestLogService());
    }

    [LinuxFact]
    public void FileCopy_rejects_source_file_symlink_outside_extraction_root()
    {
        string externalFile = Path.Combine(ExternalDirectory, "source.dat");
        File.WriteAllText(externalFile, "outside");
        File.CreateSymbolicLink(Path.Combine(ExtractionDirectory, "linked.dat"), externalFile);

        var document = LoadRule(
            """
            <Rule>
              <Source>linked.dat</Source>
              <Destination>0</Destination>
              <DestinationDirectory></DestinationDirectory>
            </Rule>
            """
        );
        var rule = new FileCopy();
        rule.Load(document.DocumentElement!);
        rule.Execute(_root, ExtractionDirectory, ModDirectory, Path.Combine(_root, "instance"));

        Assert.Equal("outside", File.ReadAllText(externalFile));
        Assert.False(File.Exists(Path.Combine(ModDirectory, "linked.dat")));
    }

    [LinuxFact]
    public void FileCopy_rejects_destination_directory_symlink_outside_mod_root()
    {
        File.WriteAllText(Path.Combine(ExtractionDirectory, "source.dat"), "payload");
        Directory.CreateSymbolicLink(Path.Combine(ModDirectory, "linked"), ExternalDirectory);

        var document = LoadRule(
            """
            <Rule>
              <Source>source.dat</Source>
              <Destination>0</Destination>
              <DestinationDirectory>linked</DestinationDirectory>
            </Rule>
            """
        );
        var rule = new FileCopy();
        rule.Load(document.DocumentElement!);
        rule.Execute(_root, ExtractionDirectory, ModDirectory, Path.Combine(_root, "instance"));

        Assert.False(File.Exists(Path.Combine(ExternalDirectory, "source.dat")));
    }

    [LinuxFact]
    public void DirectoryCopy_rejects_source_directory_symlink_outside_extraction_root()
    {
        string externalSource = Path.Combine(ExternalDirectory, "source");
        Directory.CreateDirectory(externalSource);
        File.WriteAllText(Path.Combine(externalSource, "payload.dat"), "outside");
        Directory.CreateSymbolicLink(Path.Combine(ExtractionDirectory, "linked"), externalSource);

        var document = LoadRule(
            """
            <Rule>
              <Source>linked</Source>
              <Destination>0</Destination>
              <DestinationDirectory>safe</DestinationDirectory>
              <IncludeRootDirectory>false</IncludeRootDirectory>
            </Rule>
            """
        );
        var rule = new DirectoryCopy();
        rule.Load(document.DocumentElement!);
        rule.Execute(_root, ExtractionDirectory, ModDirectory, Path.Combine(_root, "instance"));

        Assert.False(File.Exists(Path.Combine(ModDirectory, "safe", "payload.dat")));
    }

    [LinuxFact]
    public void DirectoryCopy_rejects_destination_directory_symlink_outside_mod_root()
    {
        string sourceDirectory = Path.Combine(ExtractionDirectory, "source");
        Directory.CreateDirectory(sourceDirectory);
        File.WriteAllText(Path.Combine(sourceDirectory, "payload.dat"), "payload");
        Directory.CreateSymbolicLink(Path.Combine(ModDirectory, "linked"), ExternalDirectory);

        var document = LoadRule(
            """
            <Rule>
              <Source>source</Source>
              <Destination>0</Destination>
              <DestinationDirectory>linked</DestinationDirectory>
              <IncludeRootDirectory>false</IncludeRootDirectory>
            </Rule>
            """
        );
        var rule = new DirectoryCopy();
        rule.Load(document.DocumentElement!);
        rule.Execute(_root, ExtractionDirectory, ModDirectory, Path.Combine(_root, "instance"));

        Assert.False(File.Exists(Path.Combine(ExternalDirectory, "payload.dat")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static XmlDocument LoadRule(string xml)
    {
        var document = new XmlDocument();
        document.LoadXml(xml);
        return document;
    }

    private sealed class TestLogService : ILogService
    {
        public void Log(string message) { }
        public void LineBreak() { }
    }
}
