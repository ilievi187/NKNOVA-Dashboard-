using Nolvus.Package.Mods;
using Xunit;

namespace Nolvus.Package.Tests.Mods;

public sealed class FluorinePlatformTests
{
    [Fact]
    public void Fluorine_exposes_linux_support_contract()
    {
        Assert.Equal(OperatingSystem.IsLinux(), Fluorine.IsSupported);
    }

    [NonLinuxFact]
    public async Task Unsupported_platform_rejects_install_and_launch_before_side_effects()
    {
        Assert.False(Fluorine.IsSupported);
        Assert.False(Fluorine.IsInstalled);
        Assert.False(Fluorine.IsRunning);
        Assert.Throws<PlatformNotSupportedException>(() => Fluorine.Start());
        await Assert.ThrowsAsync<PlatformNotSupportedException>(
            () => Fluorine.Install(null!, null!)
        );
    }
}

public sealed class NonLinuxFactAttribute : FactAttribute
{
    public NonLinuxFactAttribute()
    {
        if (OperatingSystem.IsLinux())
            Skip = "The unsupported-platform path requires a non-Linux test runner.";
    }
}
