using Nolvus.Package.Conditions;
using Xunit;

namespace Nolvus.Package.Tests.Conditions;

public sealed class FileExistsConditionTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"nolvus-file-exists-tests-{Guid.NewGuid():N}"
    );

    private string GameDirectory => Path.Combine(_root, "game");
    private string InstallDirectory => Path.Combine(_root, "install");

    public FileExistsConditionTests()
    {
        Directory.CreateDirectory(GameDirectory);
        Directory.CreateDirectory(InstallDirectory);
    }

    [Fact]
    public void Existing_relative_file_matches_true_expectation()
    {
        File.WriteAllText(Path.Combine(InstallDirectory, "ready.flag"), "ready");
        var condition = CreateCondition("ready.flag", expected: true, operatorValue: 0);

        Assert.True(condition.IsValid(GameDirectory, InstallDirectory));
    }

    [Fact]
    public void Source_one_checks_game_path_instead_of_install_directory()
    {
        File.WriteAllText(Path.Combine(GameDirectory, "ready.flag"), "ready");
        var condition = CreateCondition("ready.flag", expected: true, operatorValue: 0);
        condition.Source = 1;

        Assert.True(condition.IsValid(GameDirectory, InstallDirectory));
    }

    [Fact]
    public void Windows_path_separators_are_normalized()
    {
        string nestedDirectory = Path.Combine(InstallDirectory, "nested");
        Directory.CreateDirectory(nestedDirectory);
        File.WriteAllText(Path.Combine(nestedDirectory, "ready.flag"), "ready");
        var condition = CreateCondition("nested\\ready.flag", expected: true, operatorValue: 0);

        Assert.True(condition.IsValid(GameDirectory, InstallDirectory));
    }

    [Fact]
    public void Xml_load_reads_optional_source_selector()
    {
        File.WriteAllText(Path.Combine(GameDirectory, "ready.flag"), "ready");
        var document = new System.Xml.XmlDocument();
        document.LoadXml(
            """
            <Condition>
              <DataToCompare>ready.flag</DataToCompare>
              <Operator>0</Operator>
              <ValueToCompare>true</ValueToCompare>
              <Source>1</Source>
            </Condition>
            """
        );

        var condition = new FileExistsCondition();
        condition.Load(document.DocumentElement!);

        Assert.True(condition.IsValid(GameDirectory, InstallDirectory));
    }

    [Fact]
    public void Xml_load_defaults_missing_source_to_install_directory()
    {
        File.WriteAllText(Path.Combine(InstallDirectory, "ready.flag"), "ready");
        var document = new System.Xml.XmlDocument();
        document.LoadXml(
            """
            <Condition>
              <DataToCompare>ready.flag</DataToCompare>
              <Operator>0</Operator>
              <ValueToCompare>true</ValueToCompare>
            </Condition>
            """
        );

        var condition = new FileExistsCondition();
        condition.Load(document.DocumentElement!);

        Assert.True(condition.IsValid(GameDirectory, InstallDirectory));
    }

    [Theory]
    [InlineData("2")]
    [InlineData("not-a-source")]
    public void Invalid_xml_source_fails_closed(string source)
    {
        var document = new System.Xml.XmlDocument();
        document.LoadXml(
            """
            <Condition>
              <DataToCompare>ready.flag</DataToCompare>
              <Operator>0</Operator>
              <ValueToCompare>true</ValueToCompare>
              <Source>0</Source>
            </Condition>
            """
        );
        document.DocumentElement!["Source"]!.InnerText = source;

        var condition = new FileExistsCondition();
        condition.Load(document.DocumentElement!);

        Assert.False(condition.IsValid(GameDirectory, InstallDirectory));
    }

    [Fact]
    public void Missing_relative_file_matches_false_expectation()
    {
        var condition = CreateCondition("missing.flag", expected: false, operatorValue: 0);

        Assert.True(condition.IsValid(GameDirectory, InstallDirectory));
    }

    [Fact]
    public void Not_equal_operator_compares_existence_to_expected_value()
    {
        File.WriteAllText(Path.Combine(InstallDirectory, "ready.flag"), "ready");
        var condition = CreateCondition("ready.flag", expected: false, operatorValue: 1);

        Assert.True(condition.IsValid(GameDirectory, InstallDirectory));
    }

    [Fact]
    public void Invalid_expected_value_returns_false()
    {
        var condition = new FileExistsCondition
        {
            DataToCompare = "ready.flag",
            Operator = 0,
            ValueToCompare = "sometimes",
        };

        Assert.False(condition.IsValid(GameDirectory, InstallDirectory));
    }

    [Fact]
    public void Unsupported_operator_returns_false()
    {
        File.WriteAllText(Path.Combine(InstallDirectory, "ready.flag"), "ready");
        var condition = CreateCondition("ready.flag", expected: true, operatorValue: 42);

        Assert.False(condition.IsValid(GameDirectory, InstallDirectory));
    }

    [Fact]
    public void Parent_directory_traversal_outside_install_root_is_rejected()
    {
        File.WriteAllText(Path.Combine(_root, "outside.flag"), "outside");
        var condition = CreateCondition("../outside.flag", expected: true, operatorValue: 0);

        Assert.False(condition.IsValid(GameDirectory, InstallDirectory));
    }

    [Fact]
    public void Absolute_path_outside_install_root_is_rejected()
    {
        string outsideFile = Path.Combine(_root, "outside.flag");
        File.WriteAllText(outsideFile, "outside");
        var condition = CreateCondition(outsideFile, expected: true, operatorValue: 0);

        Assert.False(condition.IsValid(GameDirectory, InstallDirectory));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static FileExistsCondition CreateCondition(
        string relativePath,
        bool expected,
        int operatorValue
    ) => new()
    {
        DataToCompare = relativePath,
        Operator = operatorValue,
        ValueToCompare = expected.ToString(),
    };
}
