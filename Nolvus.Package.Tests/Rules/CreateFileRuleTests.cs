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
