using IniParser;
using Nolvus.Package.Mods;
using Xunit;

namespace Nolvus.Package.Tests.Mods;

public sealed class ModOrganizerExecutableTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"nolvus-mo-executables-{Guid.NewGuid():N}"
    );
    private string IniPath => Path.Combine(_directory, "ModOrganizer.ini");

    public ModOrganizerExecutableTests()
    {
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public void ModifyExecutable_updates_the_full_multi_digit_entry_index()
    {
        File.WriteAllText(
            IniPath,
            """
            [customExecutables]
            size=10
            1\title=First
            1\binary=first.exe
            1\arguments=first-args
            1\workingDirectory=/first
            10\title=Target
            10\binary=old.exe
            10\arguments=old-args
            10\workingDirectory=/old
            """
        );

        ModOrganizer.ModifyExecutable("Target", _directory, "new.exe", "new-args", "/new");

        var ini = new FileIniDataParser().ReadFile(IniPath);
        Assert.Equal("first.exe", ini["customExecutables"]["1\\binary"]);
        Assert.Equal("first-args", ini["customExecutables"]["1\\arguments"]);
        Assert.Equal("new.exe", ini["customExecutables"]["10\\binary"]);
        Assert.Equal("new-args", ini["customExecutables"]["10\\arguments"]);
        Assert.Equal("/new", ini["customExecutables"]["10\\workingDirectory"]);
        Assert.True(ModOrganizer.CheckIfExecutableExists("Target", _directory));
    }

    [Fact]
    public void CheckIfExecutableExists_returns_false_when_file_or_section_is_missing()
    {
        Assert.False(ModOrganizer.CheckIfExecutableExists("Target", _directory));

        File.WriteAllText(IniPath, "[other]\nvalue=Target\n");

        Assert.False(ModOrganizer.CheckIfExecutableExists("Target", _directory));
    }

    [Fact]
    public void ModifyExecutable_ignores_a_missing_title_without_changing_the_ini()
    {
        File.WriteAllText(
            IniPath,
            """
            [customExecutables]
            size=1
            1\title=Other
            1\binary=other.exe
            """
        );

        ModOrganizer.ModifyExecutable("Missing", _directory, "new.exe", "args", "/new");

        var ini = new FileIniDataParser().ReadFile(IniPath);
        Assert.Equal("other.exe", ini["customExecutables"]["1\\binary"]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
