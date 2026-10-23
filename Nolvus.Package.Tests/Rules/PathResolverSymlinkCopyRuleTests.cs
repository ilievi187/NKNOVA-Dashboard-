using System.Diagnostics;
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
    private readonly List<string> _junctionPaths = new();

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
    public void FileCopy_rejects_destination_file_symlink_outside_mod_root()
    {
        File.WriteAllText(Path.Combine(ExtractionDirectory, "source.dat"), "payload");
        string externalFile = Path.Combine(ExternalDirectory, "linked.dat");
        File.WriteAllText(externalFile, "original");
        File.CreateSymbolicLink(Path.Combine(ModDirectory, "linked.dat"), externalFile);

        var document = LoadRule(
            """
            <Rule>
              <Source>source.dat</Source>
              <Destination>0</Destination>
              <DestinationDirectory></DestinationDirectory>
              <NewFileName>linked.dat</NewFileName>
            </Rule>
            """
        );
        var rule = new FileCopy();
        rule.Load(document.DocumentElement!);
        rule.Execute(_root, ExtractionDirectory, ModDirectory, Path.Combine(_root, "instance"));

        Assert.Equal("original", File.ReadAllText(externalFile));
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

    [LinuxFact]
    public void DirectoryCopy_rejects_nested_destination_directory_symlink_outside_mod_root()
    {
        string sourceDirectory = Path.Combine(ExtractionDirectory, "source", "nested");
        Directory.CreateDirectory(sourceDirectory);
        File.WriteAllText(Path.Combine(sourceDirectory, "payload.dat"), "payload");

        string destinationDirectory = Path.Combine(ModDirectory, "safe");
        Directory.CreateDirectory(destinationDirectory);
        Directory.CreateSymbolicLink(Path.Combine(destinationDirectory, "nested"), ExternalDirectory);

        var document = LoadRule(
            """
            <Rule>
              <Source>source</Source>
              <Destination>0</Destination>
              <DestinationDirectory>safe</DestinationDirectory>
              <IncludeRootDirectory>false</IncludeRootDirectory>
            </Rule>
            """
        );
        var rule = new DirectoryCopy();
        rule.Load(document.DocumentElement!);
        rule.Execute(_root, ExtractionDirectory, ModDirectory, Path.Combine(_root, "instance"));

        Assert.False(File.Exists(Path.Combine(ExternalDirectory, "payload.dat")));
    }

    [LinuxFact]
    public void DirectoryCopy_rejects_nested_source_directory_symlink_outside_extraction_root()
    {
        string sourceDirectory = Path.Combine(ExtractionDirectory, "source");
        string externalSource = Path.Combine(ExternalDirectory, "linked");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(externalSource);
        File.WriteAllText(Path.Combine(externalSource, "payload.dat"), "outside");
        Directory.CreateSymbolicLink(Path.Combine(sourceDirectory, "linked"), externalSource);

        var document = LoadRule(
            """
            <Rule>
              <Source>source</Source>
              <Destination>0</Destination>
              <DestinationDirectory>safe</DestinationDirectory>
              <IncludeRootDirectory>false</IncludeRootDirectory>
            </Rule>
            """
        );
        var rule = new DirectoryCopy();
        rule.Load(document.DocumentElement!);
        rule.Execute(_root, ExtractionDirectory, ModDirectory, Path.Combine(_root, "instance"));

        Assert.False(File.Exists(Path.Combine(ModDirectory, "safe", "linked", "payload.dat")));
    }

    [WindowsFact]
    public void CreateFile_rejects_destination_junction_outside_mod_root()
    {
        CreateDirectoryJunction(Path.Combine(ModDirectory, "linked"), ExternalDirectory);
        var document = LoadRule(
            """
            <Rule>
              <Name>linked/escaped.ini</Name>
              <Source>0</Source>
              <DefaultText>outside</DefaultText>
            </Rule>
            """
        );
        var rule = new CreateFile();
        rule.Load(document.DocumentElement!);
        rule.Execute(_root, ExtractionDirectory, ModDirectory, Path.Combine(_root, "instance"));

        Assert.False(File.Exists(Path.Combine(ExternalDirectory, "escaped.ini")));
    }

    [WindowsFact]
    public void FileCopy_rejects_source_file_through_junction_outside_extraction_root()
    {
        string externalSource = Path.Combine(ExternalDirectory, "source");
        Directory.CreateDirectory(externalSource);
        File.WriteAllText(Path.Combine(externalSource, "source.dat"), "outside");
        CreateDirectoryJunction(Path.Combine(ExtractionDirectory, "linked"), externalSource);

        var document = LoadRule(
            """
            <Rule>
              <Source>linked/source.dat</Source>
              <Destination>0</Destination>
              <DestinationDirectory>safe</DestinationDirectory>
            </Rule>
            """
        );
        var rule = new FileCopy();
        rule.Load(document.DocumentElement!);
        rule.Execute(_root, ExtractionDirectory, ModDirectory, Path.Combine(_root, "instance"));

        Assert.False(File.Exists(Path.Combine(ModDirectory, "safe", "source.dat")));
    }

    [WindowsFact]
    public void FileCopy_rejects_destination_directory_junction_outside_mod_root()
    {
        File.WriteAllText(Path.Combine(ExtractionDirectory, "source.dat"), "payload");
        CreateDirectoryJunction(Path.Combine(ModDirectory, "linked"), ExternalDirectory);

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

    [WindowsFact]
    public void DirectoryCopy_rejects_source_directory_junction_outside_extraction_root()
    {
        string externalSource = Path.Combine(ExternalDirectory, "source");
        Directory.CreateDirectory(externalSource);
        File.WriteAllText(Path.Combine(externalSource, "payload.dat"), "outside");
        CreateDirectoryJunction(Path.Combine(ExtractionDirectory, "linked"), externalSource);

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

    [WindowsFact]
    public void DirectoryCopy_rejects_nested_destination_junction_outside_mod_root()
    {
        string sourceDirectory = Path.Combine(ExtractionDirectory, "source", "nested");
        Directory.CreateDirectory(sourceDirectory);
        File.WriteAllText(Path.Combine(sourceDirectory, "payload.dat"), "payload");

        string destinationDirectory = Path.Combine(ModDirectory, "safe");
        Directory.CreateDirectory(destinationDirectory);
        CreateDirectoryJunction(Path.Combine(destinationDirectory, "nested"), ExternalDirectory);

        var document = LoadRule(
            """
            <Rule>
              <Source>source</Source>
              <Destination>0</Destination>
              <DestinationDirectory>safe</DestinationDirectory>
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
        foreach (string junctionPath in _junctionPaths)
        {
            if (Directory.Exists(junctionPath))
                Directory.Delete(junctionPath);
        }

        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private void CreateDirectoryJunction(string junctionPath, string targetPath)
    {
        var startInfo = new ProcessStartInfo("cmd.exe")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("mklink");
        startInfo.ArgumentList.Add("/J");
        startInfo.ArgumentList.Add(junctionPath);
        startInfo.ArgumentList.Add(targetPath);

        using Process process =
            Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start cmd.exe for junction creation.");
        string standardOutput = process.StandardOutput.ReadToEnd();
        string standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Failed to create directory junction '{junctionPath}'. {standardOutput} {standardError}"
            );
        }

        _junctionPaths.Add(junctionPath);
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

public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
            Skip = "Directory junction tests require a Windows runner.";
    }
}
