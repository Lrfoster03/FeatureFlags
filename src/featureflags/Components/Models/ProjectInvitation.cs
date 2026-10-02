using System.ComponentModel.DataAnnotations;

namespace FeatureFlags.Components.Models;

public class ProjectInvitation
{
    public int Id { get; set; }
    public int Revision { get; set; } = 1;
    public string ProjectId { get; set; } = "";
    public Project Project { get; set; } = default!;
    [MaxLength(256)] public string Email { get; set; } = "";
    [MaxLength(256)] public string NormalizedEmail { get; set; } = "";
    public ProjectRole Role { get; set; } = ProjectRole.Viewer;
    public string InvitedByUserId { get; set; } = "";
    [MaxLength(64)] public string TokenHash { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime IssuedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public string? AcceptedByUserId { get; set; }
    public DateTime? RevokedAt { get; set; }
}
