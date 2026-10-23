using System.Diagnostics;
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

    [WindowsFact]
    public void FindExecutable_WindowsExplicitBackslashPath_ReturnsFullPath()
    {
        using var path = new PathEnvironment();
        using var temp = new TemporaryDirectory();
        var executable = temp.CreateExecutable("tool");
        path.Set(string.Empty);

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

    [WindowsFact]
    public void FindExecutable_WindowsSearchAppendsExecutableExtensionAndRejectsExtensionlessFiles()
    {
        using var path = new PathEnvironment();
        using var temp = new TemporaryDirectory();
        var executable = temp.CreateExecutable("wget");
        File.WriteAllText(Path.Combine(temp.Root, "not-an-executable"), "data");
        path.Set(temp.Root);

        Assert.Equal(executable, ExecutableResolver.FindExecutable("wget"));
        Assert.Null(ExecutableResolver.FindExecutable("not-an-executable"));
    }

    [WindowsFact]
    public void RequireExecutable_WindowsResolvedPathCanBeStartedByProcessStartInfo()
    {
        using var path = new PathEnvironment();
        path.Set(Environment.SystemDirectory);

        var commandProcessor = ExecutableResolver.RequireExecutable("cmd");
        Assert.Equal(".exe", Path.GetExtension(commandProcessor), ignoreCase: true);

        using var process = Process.Start(
            new ProcessStartInfo
            {
                FileName = commandProcessor,
                Arguments = "/c exit 0",
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        );

        Assert.NotNull(process);
        process!.WaitForExit();
        Assert.Equal(0, process.ExitCode);
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
            var fileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                && string.IsNullOrEmpty(Path.GetExtension(name))
                ? name + ".exe"
                : name;
            var path = Path.Combine(directory, fileName);
            File.WriteAllText(path, RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "test executable" : "#!/bin/sh\nexit 0\n");
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            return path;
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}

public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
            Skip = "This executable extension case requires Windows path semantics.";
    }
}
