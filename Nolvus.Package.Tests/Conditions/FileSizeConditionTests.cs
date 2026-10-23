using Nolvus.Package.Conditions;
using Xunit;

namespace Nolvus.Package.Tests.Conditions;

public sealed class FileSizeConditionTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"nolvus-package-tests-{Guid.NewGuid():N}"
    );

    public FileSizeConditionTests()
    {
        Directory.CreateDirectory(_temporaryDirectory);
    }

    [Fact]
    public void Missing_file_returns_false_instead_of_throwing()
    {
        var condition = new FileSizeCondition
        {
            DataToCompare = "missing.esp",
            Operator = 0,
            ValueToCompare = "1",
            Source = 0,
        };

        var result = condition.IsValid(
            Path.Combine(_temporaryDirectory, "game"),
            _temporaryDirectory
        );

        Assert.False(result);
    }

    [Fact]
    public void Windows_path_separators_are_normalized_before_file_lookup()
    {
        string nestedDirectory = Path.Combine(_temporaryDirectory, "nested", "files");
        Directory.CreateDirectory(nestedDirectory);
        File.WriteAllBytes(Path.Combine(nestedDirectory, "mod.esp"), new byte[1024]);

        var condition = new FileSizeCondition
        {
            DataToCompare = "nested\\files\\mod.esp",
            Operator = 0,
            ValueToCompare = "1",
            Source = 0,
        };

        var result = condition.IsValid(
            Path.Combine(_temporaryDirectory, "game"),
            _temporaryDirectory
        );

        Assert.True(result);
    }

    [Fact]
    public void Absolute_path_outside_source_directory_is_rejected()
    {
        string sourceDirectory = Path.Combine(_temporaryDirectory, "install");
        Directory.CreateDirectory(sourceDirectory);

        string outsideFile = Path.Combine(_temporaryDirectory, "outside.esp");
        File.WriteAllBytes(outsideFile, new byte[1024]);

        var condition = new FileSizeCondition
        {
            DataToCompare = outsideFile,
            Operator = 0,
            ValueToCompare = "1",
            Source = 0,
        };

        var result = condition.IsValid(
            Path.Combine(_temporaryDirectory, "game"),
            sourceDirectory
        );

        Assert.False(result);
    }

    [Fact]
    public void Parent_directory_traversal_outside_source_directory_is_rejected()
    {
        string sourceDirectory = Path.Combine(_temporaryDirectory, "install");
        Directory.CreateDirectory(sourceDirectory);

        string outsideFile = Path.Combine(_temporaryDirectory, "outside.esp");
        File.WriteAllBytes(outsideFile, new byte[1024]);

        var condition = new FileSizeCondition
        {
            DataToCompare = "../outside.esp",
            Operator = 0,
            ValueToCompare = "1",
            Source = 0,
        };

        var result = condition.IsValid(
            Path.Combine(_temporaryDirectory, "game"),
            sourceDirectory
        );

        Assert.False(result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-size")]
    public void Invalid_size_threshold_returns_false_instead_of_throwing(string threshold)
    {
        File.WriteAllBytes(Path.Combine(_temporaryDirectory, "mod.esp"), new byte[1024]);

        var condition = new FileSizeCondition
        {
            DataToCompare = "mod.esp",
            Operator = 0,
            ValueToCompare = threshold,
            Source = 0,
        };

        var result = condition.IsValid(
            Path.Combine(_temporaryDirectory, "game"),
            _temporaryDirectory
        );

        Assert.False(result);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
            Directory.Delete(_temporaryDirectory, recursive: true);
    }
}
