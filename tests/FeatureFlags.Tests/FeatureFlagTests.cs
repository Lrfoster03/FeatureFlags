using FeatureFlags.Components.Models;

namespace FeatureFlags.Tests;

public class FeatureFlagTests
{
    [Fact]
    public void FeatureFlag_Creation_Assigns_Provided_Values()
    {
        var featureFlag = new FeatureFlag
        {
            Name = "TestFeature",
            Description = "A test feature",
            PercentageRollout = 100
        };

        Assert.NotNull(featureFlag);
        Assert.Equal("TestFeature", featureFlag.Name);
        Assert.Equal("A test feature", featureFlag.Description);
        Assert.Equal(100, featureFlag.PercentageRollout);
        Assert.False(string.IsNullOrWhiteSpace(featureFlag.Salt));
    }

    [Fact]
    public void FeatureFlag_Defaults_Are_Empty_Name_And_Zero_Rollout()
    {
        var featureFlag = new FeatureFlag();

        Assert.NotNull(featureFlag);
        Assert.Equal(string.Empty, featureFlag.Name);
        Assert.Equal(string.Empty, featureFlag.Description);
        Assert.Equal(0, featureFlag.Id);
        Assert.Equal(0, featureFlag.PercentageRollout);
        Assert.False(string.IsNullOrWhiteSpace(featureFlag.Salt));
    }

    [Fact]
    public void FeatureFlag_Properties_Can_Be_Updated_After_Creation()
    {
        var featureFlag = new FeatureFlag();

        featureFlag.Id = 2;
        featureFlag.Description = "A test feature flag";
        featureFlag.Name = "TestFeature";
        featureFlag.PercentageRollout = 75;

        Assert.Equal(2, featureFlag.Id);
        Assert.Equal("A test feature flag", featureFlag.Description);
        Assert.Equal("TestFeature", featureFlag.Name);
        Assert.Equal(75, featureFlag.PercentageRollout);
    }

    [Fact]
    public void FeatureFlag_Constructor_Sets_Name_And_Description()
    {
        var featureFlag = new FeatureFlag("testFlag", "A test feature flag");

        Assert.NotNull(featureFlag);
        Assert.Equal("testFlag", featureFlag.Name);
        Assert.Equal("A test feature flag", featureFlag.Description);
        Assert.Equal(0, featureFlag.Id);
        Assert.Equal(0, featureFlag.PercentageRollout);
        Assert.False(string.IsNullOrWhiteSpace(featureFlag.Salt));
    }
}
