namespace FeatureFlags.Components.Models;

public sealed class AuditEvent
{
    public long Id { get; set; }
    public Guid OperationId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public string ProjectId { get; set; } = "";
    public int? EnvironmentId { get; set; }
    public string? EnvironmentName { get; set; }
    public string ActorUserId { get; set; } = "";
    public string ActorDisplayName { get; set; } = "";
    public string ActorEmail { get; set; } = "";
    public string Action { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string EntityName { get; set; } = "";
    public string? Before { get; set; }
    public string? After { get; set; }
    public int SchemaVersion { get; set; } = 1;
}
