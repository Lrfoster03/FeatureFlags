namespace FeatureFlags.Components.Models;
public class FeatureFlagsResponse
{
    public Dictionary<string, bool> FeatureFlags { get; set; } = [];
}