namespace FeatureFlags.Components.Models;

public class ProjectEnvironment
{
    public int Id { get; set; }

    public int ProjectId { get; set; }
    public Project Project { get; set; } = default!;

    public string Name { get; set; } = string.Empty; // dev, staging, production

    public List<FeatureFlag> FeatureFlags { get; set; } = [];
    public List<ClientKey> ClientKeys { get; set; } = [];
}