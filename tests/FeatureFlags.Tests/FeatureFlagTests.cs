namespace FeatureFlags.Tests;
using FeatureFlags.Components.Shared;
using FeatureFlags.Components.Models;
using FeatureFlags.Components;

public class UnitTest1
{
    [Fact]
    public void TestFeatureFlagCreation()
    {
        var featureFlag = new FeatureFlag
        {
            Name = "TestFeature",
            IsEnabled = true
        };

        Assert.NotNull(featureFlag);
        Assert.Equal("TestFeature", featureFlag.Name);
        Assert.True(featureFlag.IsEnabled);
    }

    [Fact]
    public void TestFeatureFlagDefaultValues()
    {
        var featureFlag = new FeatureFlag();

        Assert.NotNull(featureFlag);
        Assert.Equal("", featureFlag.Name);
        Assert.False(featureFlag.IsEnabled);
    }

    [Fact]
    public void TestFeatureFlagPresetValues()
    {
        var featureFlag = new FeatureFlag();

        Assert.NotNull(featureFlag);
        Assert.Equal("", featureFlag.Name);
        Assert.False(featureFlag.IsEnabled);
        Assert.Equal(0, featureFlag.Id);
        Assert.Equal("", featureFlag.Description);

        featureFlag.Id = 2;
        featureFlag.Description = "A test feature flag";
        featureFlag.Name = "TestFeature";
        featureFlag.IsEnabled = true;

        Assert.Equal(2, featureFlag.Id);
        Assert.Equal("A test feature flag", featureFlag.Description);
        Assert.Equal("TestFeature", featureFlag.Name);
        Assert.True(featureFlag.IsEnabled);
    }

    [Fact]
    public void TestFeatureFlagCreationWithValues()
    {
        var featureFlag = new FeatureFlag("testFlag", "A test feature flag", true);

        Assert.NotNull(featureFlag);
        Assert.Equal("testFlag", featureFlag.Name);
        Assert.True(featureFlag.IsEnabled);
        Assert.Equal(0, featureFlag.Id);
        Assert.Equal("A test feature flag", featureFlag.Description);
    }
}
