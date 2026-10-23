using Nolvus.Core.Misc;
using Xunit;

namespace Nolvus.Core.Tests.Misc;

public sealed class EnbLookupTests
{
    [Fact]
    public void GetEnbCode_MapsV6PresetNameToItsOwnCode()
    {
        Assert.Equal("KAUZ", ENBs.GetENBCode("Kauz"));
    }

    [Fact]
    public void GetEnbCode_DoesNotMapUnknownNameToAValidPreset()
    {
        Assert.Null(ENBs.GetENBCode("Not a shipped ENB"));
    }
}
