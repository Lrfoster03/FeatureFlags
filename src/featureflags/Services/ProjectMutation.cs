using System.Buffers.Binary;
using System.Data;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using FeatureFlags.Components.Models;
using FeatureFlags.Data;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;

namespace FeatureFlags.Services;

public abstract class ProjectMutation(
    IDbContextFactory<FeatureFlagDbContext> dbFactory,
    AuthenticationStateProvider authentication)
{
    // Deliberately non-virtual: derived actions describe changes, never saving or auditing.
    protected async Task<IReadOnlyList<AuditEvent>> ExecuteAsync(
        string projectId, Guid operationId, ProjectRole minimumRole, Func<MutationContext, Task> change,
        bool creatingProject = false, CancellationToken cancellationToken = default)
    {
        var principal = (await authentication.GetAuthenticationStateAsync()).User;
        var actorId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (principal.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(actorId))
            throw new UnauthorizedAccessException("Sign in to change this project.");
        if (string.IsNullOrWhiteSpace(projectId) || operationId == Guid.Empty)
            throw new ArgumentException("Project and operation IDs are required.");

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var member = await db.ProjectMembers.AsNoTracking().SingleOrDefaultAsync(m =>
            m.ProjectId == projectId && m.UserId == actorId && m.RevokedAt == null, cancellationToken);
        if (!creatingProject && (member is null || member.Role < minimumRole))
            throw new UnauthorizedAccessException("You do not have permission to change this project.");

        await using var transaction = await db.Database.BeginTransactionAsync(
            db.Database.IsNpgsql() ? IsolationLevel.ReadCommitted : IsolationLevel.Serializable, cancellationToken);
        if (db.Database.IsNpgsql())
        {
            // Lock across app instances, then read a fresh result after the previous transaction ends.
            var lockId = BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(operationId.ToByteArray()));
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockId})", cancellationToken);
        }

        var completed = await db.AuditEvents.AsNoTracking().Where(e => e.OperationId == operationId).ToListAsync(cancellationToken);
        if (completed.Count > 0)
        {
            if (completed.Any(e => e.ProjectId != projectId || e.ActorUserId != actorId))
                throw new UnauthorizedAccessException("This operation belongs to another project or user.");
            return completed;
        }

        var context = new MutationContext(db, projectId, actorId);
        await change(context);
        var pending = await context.PrepareAsync(cancellationToken);
        if (pending.Count == 0)
            return [];

        var writes = context.CoveredEntities;
        await db.SaveMutationAsync(writes, cancellationToken);
        var timestamp = DateTime.UtcNow;
        var events = pending.Select(p => new AuditEvent
        {
            OperationId = operationId, OccurredAtUtc = timestamp, ProjectId = projectId,
            ActorUserId = actorId,
            ActorDisplayName = member?.DisplayName is { Length: > 0 } display ? display : principal.Identity?.Name ?? actorId,
            ActorEmail = member?.Email ?? principal.FindFirstValue(ClaimTypes.Email) ?? principal.Identity?.Name ?? "",
            Action = p.Action, EntityType = AuditSnapshot.Type(p.Entity),
            EntityId = Convert.ToString(db.Entry(p.Entity).Property("Id").CurrentValue, System.Globalization.CultureInfo.InvariantCulture)!,
            EntityName = AuditSnapshot.Name(p.Entity), EnvironmentId = p.Environment?.Id,
            EnvironmentName = p.Environment?.Name, Before = p.Before, After = p.After
        }).ToList();
        db.AuditEvents.AddRange(events);
        await db.SaveMutationAsync(events.Select(e => (object)e).ToHashSet(ReferenceEqualityComparer.Instance), cancellationToken);
        try
        {
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            // A lost commit response may still mean success; reconcile using the operation ID.
            await using var verify = await dbFactory.CreateDbContextAsync(cancellationToken);
            var saved = await verify.AuditEvents.AsNoTracking().Where(e => e.OperationId == operationId &&
                e.ProjectId == projectId && e.ActorUserId == actorId).ToListAsync(cancellationToken);
            if (saved.Count == events.Count) return saved;
            throw;
        }
        return events;
    }
}

public sealed class MutationContext(FeatureFlagDbContext db, string projectId, string actorId)
{
    public FeatureFlagDbContext Db => db;
    public string ProjectId => projectId;
    public string ActorId => actorId;
    private readonly Dictionary<object, string> actions = new(ReferenceEqualityComparer.Instance);
    internal HashSet<object> CoveredEntities { get; } = new(ReferenceEqualityComparer.Instance);

    public void Record(object entity, string action)
    {
        if (!actions.TryAdd(entity, action)) throw new InvalidOperationException("A resource can be recorded only once per mutation.");
        CoveredEntities.Add(entity);
        if (entity is Project project && action == "project.created")
        {
            foreach (var child in project.Environments.Cast<object>().Concat(project.Members))
                CoveredEntities.Add(child);
        }
    }

    internal async Task<List<PendingAudit>> PrepareAsync(CancellationToken cancellationToken)
    {
        db.ChangeTracker.DetectChanges();
        var changed = db.ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToList();
        foreach (var entry in changed)
        {
            if (entry.Entity is AuditEvent || !CoveredEntities.Contains(entry.Entity))
                throw new InvalidOperationException("Every changed resource requires audit coverage.");
            _ = AuditSnapshot.Type(entry.Entity); // Reject new entity types until a policy exists.
            if (entry.State == EntityState.Modified && entry.Property("Revision").IsModified)
                throw new InvalidOperationException("Revision numbers are managed by the save lifecycle.");
            if (entry.State == EntityState.Modified && entry.Properties.Any(p => p.IsModified &&
                    !AuditSnapshot.EditableProperties(entry.Entity).Contains(p.Metadata.Name)))
                throw new InvalidOperationException("This field change requires an explicit audit policy.");
        }

        var result = new List<PendingAudit>();
        foreach (var (entity, action) in actions)
        {
            var entry = db.Entry(entity);
            if (entry.State == EntityState.Unchanged) continue;
            var original = entry.State == EntityState.Added ? null : entry.OriginalValues.ToObject();
            var before = original is null ? null : AuditSnapshot.Json(original);
            var after = entry.State == EntityState.Deleted ? null : AuditSnapshot.Json(entity);
            if (JsonNode.DeepEquals(before, after))
            {
                entry.State = EntityState.Unchanged;
                continue;
            }
            AuditSnapshot.ValidateAction(entity, original, entry.State, action);
            var environmentId = entity switch
            {
                FeatureFlag flag => flag.ProjectEnvironmentId,
                FeatureConfig config => config.ProjectEnvironmentId,
                ClientKey key => key.ProjectEnvironmentId,
                ProjectEnvironment env => env.Id,
                _ => (int?)null
            };
            ProjectEnvironment? environment = null;
            if (environmentId is not null)
            {
                environment = await db.ProjectEnvironments.AsNoTracking().SingleOrDefaultAsync(e =>
                    e.Id == environmentId && e.ProjectId == projectId, cancellationToken)
                    ?? throw new UnauthorizedAccessException("The resource does not belong to this project.");
            }
            if (entity is Project p && p.Id != projectId || entity is ProjectMember m && m.ProjectId != projectId)
                throw new UnauthorizedAccessException("The resource does not belong to this project.");
            if (entity is Project created && action == "project.created" &&
                (created.Environments.Any(e => e.ProjectId != projectId) || created.Members.Any(m => m.ProjectId != projectId)))
                throw new InvalidOperationException("Project provisioning cannot cover another project's resources.");
            result.Add(new(entity, action, before?.ToJsonString(), after?.ToJsonString(), environment));
        }
        return result;
    }
}

internal sealed record PendingAudit(object Entity, string Action, string? Before, string? After, ProjectEnvironment? Environment);

internal static class AuditSnapshot
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public static string[] EditableProperties(object entity) => entity switch
    {
        FeatureFlag => ["Name", "Description", "IsEnabled", "PercentageRollout"],
        FeatureConfig => ["Name", "Description", "Value", "Schema"],
        ProjectMember => ["Email", "DisplayName", "Role", "RevokedAt"],
        ClientKey => ["RevokedAt"],
        _ => []
    };
    public static string Type(object entity) => entity switch
    {
        Project => "project", ProjectEnvironment => "environment", ProjectMember => "member",
        FeatureFlag => "flag", FeatureConfig => "config", ClientKey => "key",
        _ => throw new InvalidOperationException("This resource has no audit snapshot policy.")
    };
    public static string Name(object entity) => entity switch
    {
        Project p => p.Name, ProjectEnvironment e => e.Name, ProjectMember m => m.Email,
        FeatureFlag f => f.Name, FeatureConfig c => c.Name, ClientKey k => k.Name,
        _ => throw new InvalidOperationException("Unknown audit resource.")
    };
    public static JsonNode Json(object entity) => JsonSerializer.SerializeToNode(entity switch
    {
        FeatureFlag f => (object)new { f.Name, f.Description, f.IsEnabled, f.PercentageRollout },
        FeatureConfig c => new { c.Name, c.Description, c.Schema, c.Value },
        ProjectMember m => new { m.Email, m.DisplayName, Role = m.Role.ToString(), Active = m.RevokedAt == null },
        ClientKey k => new { k.Name, Active = k.RevokedAt == null },
        Project p => new { p.Name, Environments = p.Environments.Select(e => e.Name), Owners = p.Members.Select(m => m.Email) },
        ProjectEnvironment e => new { e.Name },
        _ => throw new InvalidOperationException("This resource has no audit snapshot policy.")
    }, Options)!;

    public static void ValidateAction(object entity, object? before, EntityState state, string action)
    {
        var expected = entity switch
        {
            Project when state == EntityState.Added => "project.created",
            FeatureFlag => "flag." + Verb(state),
            FeatureConfig => "config." + Verb(state),
            ProjectMember when state == EntityState.Added => "member.added",
            ProjectMember m when before is ProjectMember old && old.RevokedAt != null && m.RevokedAt == null => "member.restored",
            ProjectMember m when before is ProjectMember old && old.RevokedAt == null && m.RevokedAt != null => "member.removed",
            ProjectMember m when before is ProjectMember old && old.Role != m.Role => "member.role_changed",
            ClientKey when state == EntityState.Added => "key.created",
            ClientKey k when before is ClientKey old && old.RevokedAt == null && k.RevokedAt != null => "key.revoked",
            _ => throw new InvalidOperationException("This change requires a new audit action policy.")
        };
        if (action != expected) throw new InvalidOperationException("Audit action does not describe the persisted change.");
    }
    private static string Verb(EntityState state) => state switch
    {
        EntityState.Added => "created", EntityState.Modified => "updated", EntityState.Deleted => "deleted",
        _ => throw new InvalidOperationException("No resource change to audit.")
    };
}
