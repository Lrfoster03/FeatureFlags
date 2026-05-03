namespace FeatureFlags.Components.Models;

public class ClientKey
{
    public int Id { get; set; }

    public int ProjectEnvironmentId { get; set; }
    public ProjectEnvironment ProjectEnvironment { get; set; } = default!;

    public string Name { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RevokedAt { get; set; }
}