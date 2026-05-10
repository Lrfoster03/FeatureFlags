using System.Text.Json.Nodes;

namespace FeatureFlags.Components.Models;
public class FeatureFlagsResponse
{
    public Dictionary<string, bool> FeatureFlags { get; set; } = [];
    public Dictionary<string, JsonObject> Configs { get; set; } = [];
}