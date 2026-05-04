namespace FeatureFlags.Components.Models;

public class ProjectMember
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public int ProjectId { get; set; }
    public Project Project { get; set; } = default!;

    public ProjectRole Role { get; set; } = ProjectRole.Viewer;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? RevokedAt { get; set; }
}