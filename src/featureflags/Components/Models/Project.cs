namespace FeatureFlags.Components.Models;

public class Project
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    public List<ProjectEnvironment> Environments { get; set; } = [];

    public List<ProjectMember> Members { get; set; } = [];
}