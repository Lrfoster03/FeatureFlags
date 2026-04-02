using Bunit;
using Xunit;
using FeatureFlags.Components.Shared;
using FeatureFlags.Components.Models;

namespace FeatureFlags.Tests;

public class PillTests : TestContext
{
    [Fact]
    public void Pill_Renders_FeatureFlag_Name()
    {
        var flag = new FeatureFlag
        {
            Name = "MyTestFlag",
            Description = "Test description",
            IsEnabled = true
        };

        var cut = RenderComponent<Pill>(parameters => parameters
            .Add(p => p.FeatureFlag, flag));

        cut.Markup.Contains("MyTestFlag");
    }
}