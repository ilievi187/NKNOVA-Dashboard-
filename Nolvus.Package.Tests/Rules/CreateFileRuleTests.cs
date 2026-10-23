using System.Xml;
using Nolvus.Package.Rules;
using Xunit;

namespace Nolvus.Package.Tests.Rules;

public sealed class CreateFileRuleTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"nolvus-create-file-tests-{Guid.NewGuid():N}"
    );

    private string GameDirectory => Path.Combine(_root, "game");
    private string ExtractionDirectory => Path.Combine(_root, "extract");
    private string ModDirectory => Path.Combine(_root, "mods");
    private string InstanceDirectory => Path.Combine(_root, "instance");

    public CreateFileRuleTests()
    {
        Directory.CreateDirectory(GameDirectory);
        Directory.CreateDirectory(ExtractionDirectory);
        Directory.CreateDirectory(ModDirectory);
        Directory.CreateDirectory(InstanceDirectory);
    }

    [Fact]
    public void Xml_rule_creates_a_nested_file_inside_the_selected_mod_root()
    {
        var rule = LoadRule("settings/profile.ini", source: 0, defaultText: "profile");

        rule.Execute(GameDirectory, ExtractionDirectory, ModDirectory, InstanceDirectory);

        Assert.Equal(
            "profile",
            File.ReadAllText(Path.Combine(ModDirectory, "settings", "profile.ini"))
        );
    }

    [Fact]
    public void Xml_rule_rejects_parent_traversal_outside_the_selected_mod_root()
    {
        var rule = LoadRule("../escaped.ini", source: 0, defaultText: "outside");

        rule.Execute(GameDirectory, ExtractionDirectory, ModDirectory, InstanceDirectory);

        Assert.False(File.Exists(Path.Combine(_root, "escaped.ini")));
    }

    [LinuxFact]
    public void Xml_rule_rejects_a_symlink_inside_the_mod_root_that_targets_outside()
    {
        string externalDirectory = Path.Combine(_root, "external");
        Directory.CreateDirectory(externalDirectory);
        Directory.CreateSymbolicLink(Path.Combine(ModDirectory, "linked"), externalDirectory);

        var rule = LoadRule("linked/escaped.ini", source: 0, defaultText: "outside");
        rule.Execute(GameDirectory, ExtractionDirectory, ModDirectory, InstanceDirectory);

        Assert.False(File.Exists(Path.Combine(externalDirectory, "escaped.ini")));
    }

    [LinuxFact]
    public void Xml_rule_rejects_a_dangling_directory_symlink_that_targets_outside()
    {
        string externalDirectory = Path.Combine(_root, "external");
        string missingExternalDirectory = Path.Combine(externalDirectory, "not-created-yet");
        Directory.CreateDirectory(externalDirectory);
        Directory.CreateSymbolicLink(Path.Combine(ModDirectory, "dangling"), missingExternalDirectory);

        var rule = LoadRule("dangling/nested/escaped.ini", source: 0, defaultText: "outside");
        rule.Execute(GameDirectory, ExtractionDirectory, ModDirectory, InstanceDirectory);

        Assert.False(File.Exists(Path.Combine(missingExternalDirectory, "nested", "escaped.ini")));
    }

    [LinuxFact]
    public void Xml_rule_rejects_a_symlink_whose_target_contains_an_outside_symlink_ancestor()
    {
        string externalDirectory = Path.Combine(_root, "external");
        Directory.CreateDirectory(externalDirectory);
        Directory.CreateSymbolicLink(Path.Combine(ModDirectory, "outside-alias"), externalDirectory);
        Directory.CreateSymbolicLink(
            Path.Combine(ModDirectory, "indirect"),
            Path.Combine(ModDirectory, "outside-alias", "nested")
        );

        var rule = LoadRule("indirect/escaped.ini", source: 0, defaultText: "outside");
        rule.Execute(GameDirectory, ExtractionDirectory, ModDirectory, InstanceDirectory);

        Assert.False(File.Exists(Path.Combine(externalDirectory, "nested", "escaped.ini")));
    }

    [LinuxFact]
    public void Xml_rule_rejects_a_file_symlink_that_targets_outside_the_mod_root()
    {
        string externalFile = Path.Combine(_root, "external.ini");
        File.WriteAllText(externalFile, "external");
        File.CreateSymbolicLink(Path.Combine(ModDirectory, "linked.ini"), externalFile);

        var rule = LoadRule("linked.ini", source: 0, defaultText: "overwritten");
        rule.Execute(GameDirectory, ExtractionDirectory, ModDirectory, InstanceDirectory);

        Assert.Equal("external", File.ReadAllText(externalFile));
    }

    [LinuxFact]
    public void Xml_rule_allows_a_symlink_that_resolves_to_a_location_inside_the_mod_root()
    {
        string internalDirectory = Path.Combine(ModDirectory, "actual");
        Directory.CreateDirectory(internalDirectory);
        Directory.CreateSymbolicLink(Path.Combine(ModDirectory, "linked"), internalDirectory);

        var rule = LoadRule("linked/created.ini", source: 0, defaultText: "inside");
        rule.Execute(GameDirectory, ExtractionDirectory, ModDirectory, InstanceDirectory);

        Assert.Equal("inside", File.ReadAllText(Path.Combine(internalDirectory, "created.ini")));
    }

    [Fact]
    public void Xml_rule_allows_parent_segments_that_resolve_back_inside_the_selected_mod_root()
    {
        var rule = LoadRule("nested/../created.ini", source: 0, defaultText: "inside");

        rule.Execute(GameDirectory, ExtractionDirectory, ModDirectory, InstanceDirectory);

        Assert.Equal("inside", File.ReadAllText(Path.Combine(ModDirectory, "created.ini")));
    }

    [Fact]
    public void Source_one_creates_the_file_inside_the_game_root()
    {
        var rule = LoadRule("config/created.ini", source: 1, defaultText: "game");

        rule.Execute(GameDirectory, ExtractionDirectory, ModDirectory, InstanceDirectory);

        Assert.Equal("game", File.ReadAllText(Path.Combine(GameDirectory, "config", "created.ini")));
    }

    [Fact]
    public void Unsupported_source_does_not_create_a_file()
    {
        var rule = LoadRule("created.ini", source: 2, defaultText: "invalid");

        rule.Execute(GameDirectory, ExtractionDirectory, ModDirectory, InstanceDirectory);

        Assert.False(File.Exists(Path.Combine(ModDirectory, "created.ini")));
        Assert.False(File.Exists(Path.Combine(GameDirectory, "created.ini")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static CreateFile LoadRule(string name, int source, string defaultText)
    {
        var document = new XmlDocument();
        document.LoadXml(
            $"""
            <Rule>
              <Name>{XmlEscape(name)}</Name>
              <Source>{source}</Source>
              <DefaultText>{XmlEscape(defaultText)}</DefaultText>
            </Rule>
            """
        );

        var rule = new CreateFile();
        rule.Load(document.DocumentElement!);
        return rule;
    }

    private static string XmlEscape(string value) =>
        System.Security.SecurityElement.Escape(value) ?? string.Empty;
}

public sealed class LinuxFactAttribute : FactAttribute
{
    public LinuxFactAttribute()
    {
        if (!OperatingSystem.IsLinux())
            Skip = "This symlink integration case runs on Linux; Windows path tests cover traversal separately.";
    }
}
