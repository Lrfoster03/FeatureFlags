using System.Text.Json.Nodes;

namespace FeatureFlags.Components.Models;

public class FeatureConfig : IConfig
{
    public int Id { get; set; }

    public int ProjectEnvironmentId { get; set; }
    public ProjectEnvironment ProjectEnvironment { get; set; } = default!;

    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public JsonObject Schema { get; set; } = [];

    public JsonObject Value { get; set; } = [];
}
