using Nolvus.Core.Utils;
using System.Runtime.InteropServices;
using Xunit;

namespace Nolvus.Core.Tests.Utils;

public sealed class ExecutableResolverTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FindExecutable_BlankInput_ReturnsNull(string? name)
    {
        using var path = new PathEnvironment();

        Assert.Null(ExecutableResolver.FindExecutable(name!));
    }

    [Fact]
    public void FindExecutable_ExplicitExecutablePath_ReturnsFullPath()
    {
        using var path = new PathEnvironment();
        using var temp = new TemporaryDirectory();
        var executable = temp.CreateExecutable("tool");
        path.Set("/nonexistent");

        Assert.Equal(executable, ExecutableResolver.FindExecutable(executable));
    }

    [Fact]
    public void FindExecutable_WhenNameIsInMultiplePathEntries_ReturnsFirstMatch()
    {
        using var path = new PathEnvironment();
        using var temp = new TemporaryDirectory();
        var firstDirectory = temp.CreateDirectory("first");
        var secondDirectory = temp.CreateDirectory("second");
        var firstExecutable = temp.CreateExecutable(firstDirectory, "tool");
        temp.CreateExecutable(secondDirectory, "tool");
        path.Set(string.Join(Path.PathSeparator, firstDirectory, secondDirectory));

        Assert.Equal(firstExecutable, ExecutableResolver.FindExecutable("tool"));
    }

    [Fact]
    public void FindExecutable_MissingExecutable_ReturnsNull()
    {
        using var path = new PathEnvironment();
        using var temp = new TemporaryDirectory();
        path.Set(temp.Root);

        Assert.Null(ExecutableResolver.FindExecutable("missing-tool"));
    }

    [Fact]
    public void RequireExecutable_MissingExecutable_ThrowsFileNotFoundException()
    {
        using var path = new PathEnvironment();
        using var temp = new TemporaryDirectory();
        path.Set(temp.Root);

        Assert.Throws<FileNotFoundException>(() => ExecutableResolver.RequireExecutable("missing-tool"));
    }

    private sealed class PathEnvironment : IDisposable
    {
        private readonly string? originalPath = Environment.GetEnvironmentVariable("PATH");

        public void Set(string value) => Environment.SetEnvironmentVariable("PATH", value);

        public void Dispose() => Environment.SetEnvironmentVariable("PATH", originalPath);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("executable-resolver-").FullName;

        public string CreateDirectory(string name)
        {
            var directory = Path.Combine(Root, name);
            Directory.CreateDirectory(directory);
            return directory;
        }

        public string CreateExecutable(string name) => CreateExecutable(Root, name);

        public string CreateExecutable(string directory, string name)
        {
            var path = Path.Combine(directory, name);
            File.WriteAllText(path, "#!/bin/sh\nexit 0\n");
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            return path;
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
