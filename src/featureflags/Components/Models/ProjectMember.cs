namespace FeatureFlags.Components.Models;

public class ProjectMember
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty; // Identity user id

    public int ProjectId { get; set; }
    public Project Project { get; set; } = default!;

    public ProjectRole Role { get; set; }
}

public enum ProjectRole
{
    Viewer,
    Editor,
    Admin,
    Owner
}