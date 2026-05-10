namespace FeatureFlags.Components.Models;

public class Project
{
    public string Id { get; private set; }  = Guid.NewGuid().ToString("N"); // 32‑character hex id
    public string Name { get; set; } = string.Empty;

    public List<ProjectEnvironment> Environments { get; set; } = [];

    public List<ProjectMember> Members { get; set; } = [];
}