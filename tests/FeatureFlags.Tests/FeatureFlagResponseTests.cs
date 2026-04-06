namespace FeatureFlags.Tests;
using FeatureFlags.Components.Shared;
using FeatureFlags.Components.Models;
using FeatureFlags.Components;

public class FeatureFlagResponseTests
{
    [Fact]
    public void TestFeatureFlagResponseCreation()
    {
        var response = new FeatureFlagsResponse();

        Assert.NotNull(response);
        Assert.NotNull(response.FeatureFlags);
        Assert.Empty(response.FeatureFlags);

        response.FeatureFlags["TestFlag"] = true;
        Assert.Single(response.FeatureFlags);
        Assert.True(response.FeatureFlags["TestFlag"]);
    }
}