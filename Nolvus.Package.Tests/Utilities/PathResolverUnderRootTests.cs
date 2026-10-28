using System.Diagnostics;
using Nolvus.Package.Tests.Rules;
using Nolvus.Package.Utilities;
using Xunit;

namespace Nolvus.Package.Tests.Utilities;

/// <summary>
/// Direct unit tests for <see cref="PathResolver.TryResolvePathUnderRoot"/>, the single
/// security boundary that FileCopy, DirectoryCopy, CreateFile and the file-exists/size
/// conditions all rely on to keep every mod path inside the root it was given. The rules
/// cover this indirectly; these tests pin the contract of the guard itself.
/// </summary>
public sealed class PathResolverUnderRootTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"nolvus-pathresolver-under-root-{Guid.NewGuid():N}"
    );
    private readonly List<string> _junctions = new();

    private string InstallRoot => Path.Combine(_root, "install");
    private string ExternalDirectory => Path.Combine(_root, "external");

    public PathResolverUnderRootTests()
    {
        Directory.CreateDirectory(InstallRoot);
        Directory.CreateDirectory(ExternalDirectory);
    }

    [Fact]
    public void Existing_file_inside_root_resolves_to_absolute_path()
    {
        string file = Path.Combine(InstallRoot, "mod.esp");
        File.WriteAllText(file, "payload");

        bool ok = PathResolver.TryResolvePathUnderRoot(InstallRoot, "mod.esp", out string resolved);

        Assert.True(ok);
        Assert.Equal(Path.GetFullPath(file), Path.GetFullPath(resolved));
    }

    [Fact]
    public void Nested_existing_directory_inside_root_resolves()
    {
        string dir = Path.Combine(InstallRoot, "a", "b");
        Directory.CreateDirectory(dir);
        string relative = Path.Combine("a", "b");

        bool ok = PathResolver.TryResolvePathUnderRoot(InstallRoot, relative, out string resolved);

        Assert.True(ok);
        Assert.Equal(Path.GetFullPath(dir), Path.GetFullPath(resolved));
    }

    [Fact]
    public void Future_write_destination_that_does_not_exist_yet_is_allowed()
    {
        // A non-existent leaf is a legitimate target for a future write, not an escape.
        string relative = Path.Combine("new", "sub", "target.dat");
        bool ok = PathResolver.TryResolvePathUnderRoot(InstallRoot, relative, out _);

        Assert.True(ok);
    }

    [Fact]
    public void Dot_segments_are_ignored_and_stay_inside_root()
    {
        string file = Path.Combine(InstallRoot, "mod.esp");
        File.WriteAllText(file, "payload");

        Assert.True(PathResolver.TryResolvePathUnderRoot(InstallRoot, "./mod.esp", out _));
        Assert.True(PathResolver.TryResolvePathUnderRoot(InstallRoot, "sub/../mod.esp", out _));
    }

    [Fact]
    public void Forward_slash_is_accepted_as_a_separator_on_all_platforms()
    {
        // .NET normalizes '/' as a directory separator on both Windows and Linux, so a
        // forward-slash relative path must resolve identically to a backslash one.
        string dir = Path.Combine(InstallRoot, "a", "b");
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, "payload.dat");
        File.WriteAllText(file, "payload");

        bool ok = PathResolver.TryResolvePathUnderRoot(InstallRoot, "a/b/payload.dat", out string resolved);

        Assert.True(ok);
        Assert.Equal(Path.GetFullPath(file), Path.GetFullPath(resolved));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_root_or_path_fails_closed(string value)
    {
        File.WriteAllText(Path.Combine(InstallRoot, "mod.esp"), "payload");

        Assert.False(PathResolver.TryResolvePathUnderRoot(value, "mod.esp", out _));
        Assert.False(PathResolver.TryResolvePathUnderRoot(InstallRoot, value, out _));
    }

    [Fact]
    public void Absolute_path_is_rejected_even_when_inside_root()
    {
        string file = Path.Combine(InstallRoot, "mod.esp");
        File.WriteAllText(file, "payload");

        Assert.False(
            PathResolver.TryResolvePathUnderRoot(InstallRoot, file, out _),
            "An absolute path must never be honored, even when it resolves inside the root."
        );
    }

    [Theory]
    [InlineData("../external/file.dat")]
    [InlineData("..")]
    [InlineData("sub/../../external/file.dat")]
    [InlineData("a/b/../../../../external/file.dat")]
    public void Parent_traversal_that_escapes_root_is_rejected(string relativePath)
    {
        File.WriteAllText(Path.Combine(ExternalDirectory, "file.dat"), "outside");

        Assert.False(PathResolver.TryResolvePathUnderRoot(InstallRoot, relativePath, out _));
    }

    [Fact]
    public void Traversal_that_stays_within_root_is_accepted()
    {
        string file = Path.Combine(InstallRoot, "mod.esp");
        Directory.CreateDirectory(Path.Combine(InstallRoot, "sub"));
        File.WriteAllText(file, "payload");

        Assert.True(PathResolver.TryResolvePathUnderRoot(InstallRoot, "sub/../mod.esp", out _));
    }

    [Fact]
    public void Resolved_path_is_populated_to_empty_when_rejected()
    {
        File.WriteAllText(Path.Combine(ExternalDirectory, "file.dat"), "outside");

        bool ok = PathResolver.TryResolvePathUnderRoot(InstallRoot, "../file.dat", out string resolved);

        Assert.False(ok);
        Assert.Equal(string.Empty, resolved);
    }

    [LinuxFact]
    public void File_symlink_pointing_outside_root_is_rejected()
    {
        string externalFile = Path.Combine(ExternalDirectory, "secret.dat");
        File.WriteAllText(externalFile, "outside");
        string link = Path.Combine(InstallRoot, "linked.dat");
        File.CreateSymbolicLink(link, externalFile);

        bool ok = PathResolver.TryResolvePathUnderRoot(InstallRoot, "linked.dat", out string resolved);

        Assert.False(ok);
        // The external file must not be exposed through the link.
        Assert.NotEqual(Path.GetFullPath(externalFile), resolved);
    }

    [LinuxFact]
    public void Directory_symlink_pointing_outside_root_is_rejected()
    {
        string externalFile = Path.Combine(ExternalDirectory, "payload.dat");
        File.WriteAllText(externalFile, "outside");
        Directory.CreateSymbolicLink(Path.Combine(InstallRoot, "linked"), ExternalDirectory);

        bool ok = PathResolver.TryResolvePathUnderRoot(InstallRoot, "linked/payload.dat", out _);

        Assert.False(ok);
    }

    [LinuxFact]
    public void Symlink_whose_target_is_itself_inside_root_is_accepted()
    {
        string realDir = Path.Combine(InstallRoot, "real");
        Directory.CreateDirectory(realDir);
        string target = Path.Combine(realDir, "payload.dat");
        File.WriteAllText(target, "inside");
        Directory.CreateSymbolicLink(Path.Combine(InstallRoot, "linked"), realDir);

        bool ok = PathResolver.TryResolvePathUnderRoot(InstallRoot, "linked/payload.dat", out string resolved);

        Assert.True(ok);
        Assert.Equal(Path.GetFullPath(target), Path.GetFullPath(resolved));
    }

    [LinuxFact]
    public void Chained_symlinks_ending_outside_root_are_rejected()
    {
        string externalFile = Path.Combine(ExternalDirectory, "deep.dat");
        File.WriteAllText(externalFile, "outside");

        string mid = Path.Combine(InstallRoot, "mid");
        string first = Path.Combine(InstallRoot, "first");
        // first -> mid (a symlink), mid -> external file (a symlink). Resolving must walk the chain.
        File.CreateSymbolicLink(first, mid);
        File.CreateSymbolicLink(mid, externalFile);

        Assert.False(PathResolver.TryResolvePathUnderRoot(InstallRoot, "first", out _));
    }

    [WindowsFact]
    public void Destination_junction_outside_root_is_rejected()
    {
        CreateDirectoryJunction(Path.Combine(InstallRoot, "linked"), ExternalDirectory);

        Assert.False(PathResolver.TryResolvePathUnderRoot(InstallRoot, "linked/escaped.ini", out _));
    }

    public void Dispose()
    {
        foreach (string junction in _junctions)
        {
            if (Directory.Exists(junction))
                Directory.Delete(junction);
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
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"mklink failed: {output} {error}");

        _junctions.Add(junctionPath);
    }
}
