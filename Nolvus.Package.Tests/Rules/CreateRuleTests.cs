using Nolvus.Package.Rules;
using Xunit;

namespace Nolvus.Package.Tests.Rules;

public sealed class CreateRuleTests
{
    [Fact]
    public void CreateRule_is_an_abstract_base_for_rules_that_implement_execute()
    {
        Assert.True(typeof(CreateRule).IsAbstract);
    }

    [Fact]
    public void CreateFile_remains_a_concrete_CreateRule()
    {
        Assert.False(typeof(CreateFile).IsAbstract);
        Assert.IsAssignableFrom<CreateRule>(new CreateFile());
    }
}
