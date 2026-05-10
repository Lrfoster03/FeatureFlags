using System.Text.Json.Nodes;

namespace FeatureFlags.Components.Models;

public interface IConfig
{
    public int Id { get; set; }

    public string Name { get; set; }
    public string Description { get; set; }
}