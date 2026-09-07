using System.Security.Cryptography;
using FeatureFlags.Components.Models;
using FeatureFlags.Data;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;

namespace FeatureFlags.Services;

public sealed class ProjectChanges(
    IDbContextFactory<FeatureFlagDbContext> dbFactory,
    IDbContextFactory<ApplicationDbContext> identityFactory,
    AuthenticationStateProvider authentication) : ProjectMutation(dbFactory, authentication)
{
    public Task<IReadOnlyList<AuditEvent>> AddItemAsync(string projectId, int environmentId, bool config, Guid operationId)
        => ExecuteAsync(projectId, operationId, ProjectRole.Editor, async context =>
        {
            await EnsureEnvironment(context, environmentId);
            var names = config
                ? await context.Db.Configs.Where(f => f.ProjectEnvironmentId == environmentId).Select(f => f.Name).ToListAsync()
                : await context.Db.FeatureFlags.Where(f => f.ProjectEnvironmentId == environmentId).Select(f => f.Name).ToListAsync();
            var count = names.Count + 1;
            var prefix = config ? "Config" : "Item";
            while (names.Contains($"{prefix} {count}")) count++;
            if (config)
            {
                var item = new FeatureConfig { Name = $"{prefix} {count}", ProjectEnvironmentId = environmentId };
                context.Db.Configs.Add(item);
                context.Record(item, "config.created");
            }
            else
            {
                var item = new FeatureFlag { Name = $"{prefix} {count}", ProjectEnvironmentId = environmentId };
                context.Db.FeatureFlags.Add(item);
                context.Record(item, "flag.created");
            }
        });

    public Task<IReadOnlyList<AuditEvent>> SaveItemsAsync(string projectId, IReadOnlyCollection<IConfig> drafts, Guid operationId)
        => ExecuteAsync(projectId, operationId, ProjectRole.Editor, async context =>
        {
            foreach (var draft in drafts)
            {
                var name = ValidateName(draft.Name);
                if (draft is FeatureFlag flag)
                {
                    if (flag.PercentageRollout is < 0 or > 100) throw new ArgumentException("Rollout must be between 0 and 100 percent.");
                    var saved = await context.Db.FeatureFlags.SingleOrDefaultAsync(f => f.Id == flag.Id && f.ProjectEnvironment.ProjectId == projectId)
                        ?? throw new ArgumentException($"Feature flag '{flag.Name}' was not found.");
                    CheckRevision(saved.Revision, flag.Revision);
                    if (await context.Db.FeatureFlags.AnyAsync(f => f.ProjectEnvironmentId == saved.ProjectEnvironmentId && f.Name == name && f.Id != saved.Id))
                        throw new ArgumentException($"A feature flag named '{name}' already exists.");
                    saved.Name = name; saved.Description = flag.Description;
                    saved.IsEnabled = flag.IsEnabled; saved.PercentageRollout = flag.PercentageRollout;
                    context.Record(saved, "flag.updated");
                }
                else if (draft is FeatureConfig config)
                {
                    if (!ConfigValidation.ValidateAgainstSchema(config.Value, config.Schema, out var error))
                        throw new ArgumentException(error);
                    var saved = await context.Db.Configs.SingleOrDefaultAsync(c => c.Id == config.Id && c.ProjectEnvironment.ProjectId == projectId)
                        ?? throw new ArgumentException($"Feature config '{config.Name}' was not found.");
                    CheckRevision(saved.Revision, config.Revision);
                    if (await context.Db.Configs.AnyAsync(c => c.ProjectEnvironmentId == saved.ProjectEnvironmentId && c.Name == name && c.Id != saved.Id))
                        throw new ArgumentException($"A feature config named '{name}' already exists.");
                    saved.Name = name; saved.Description = config.Description;
                    saved.Value = config.Value.DeepClone().AsObject(); saved.Schema = config.Schema.DeepClone().AsObject();
                    context.Record(saved, "config.updated");
                }
                else throw new ArgumentException("Unsupported configuration type.");
            }
        });

    public Task<IReadOnlyList<AuditEvent>> DeleteItemAsync(string projectId, IConfig draft, Guid operationId)
        => ExecuteAsync(projectId, operationId, ProjectRole.Editor, async context =>
        {
            if (draft is FeatureFlag flag)
            {
                var saved = await context.Db.FeatureFlags.SingleOrDefaultAsync(f => f.Id == flag.Id && f.ProjectEnvironment.ProjectId == projectId)
                    ?? throw new ArgumentException($"Feature flag '{flag.Name}' was not found.");
                CheckRevision(saved.Revision, flag.Revision);
                context.Db.FeatureFlags.Remove(saved); context.Record(saved, "flag.deleted");
            }
            else if (draft is FeatureConfig config)
            {
                var saved = await context.Db.Configs.SingleOrDefaultAsync(c => c.Id == config.Id && c.ProjectEnvironment.ProjectId == projectId)
                    ?? throw new ArgumentException($"Feature config '{config.Name}' was not found.");
                CheckRevision(saved.Revision, config.Revision);
                context.Db.Configs.Remove(saved); context.Record(saved, "config.deleted");
            }
            else throw new ArgumentException("Unsupported configuration type.");
        });

    public Task<IReadOnlyList<AuditEvent>> AddMemberAsync(string projectId, string email, ProjectRole role, Guid operationId)
        => ExecuteAsync(projectId, operationId, ProjectRole.Admin, async context =>
        {
            ValidateRole(role);
            await using var identity = await identityFactory.CreateDbContextAsync();
            var normalized = email.Trim().ToUpperInvariant();
            var user = await identity.Users.AsNoTracking().SingleOrDefaultAsync(u => u.NormalizedEmail == normalized)
                ?? throw new ArgumentException("No registered user found with that email.");
            var member = await context.Db.ProjectMembers.SingleOrDefaultAsync(m => m.ProjectId == projectId && m.UserId == user.Id);
            if (member is not null && member.RevokedAt == null) throw new ArgumentException("That user is already a member of this project.");
            var restoring = member is not null;
            if (member is null)
            {
                member = new ProjectMember { ProjectId = projectId, UserId = user.Id };
                context.Db.ProjectMembers.Add(member);
            }
            member.Email = user.Email ?? email.Trim(); member.DisplayName = user.UserName ?? member.Email;
            member.Role = role; member.RevokedAt = null;
            context.Record(member, restoring ? "member.restored" : "member.added");
        });

    public Task<IReadOnlyList<AuditEvent>> ChangeMemberAsync(string projectId, int memberId, int revision, ProjectRole? role, Guid operationId)
        => ExecuteAsync(projectId, operationId, ProjectRole.Admin, async context =>
        {
            var member = await context.Db.ProjectMembers.SingleOrDefaultAsync(m => m.Id == memberId && m.ProjectId == projectId)
                ?? throw new ArgumentException("Project member was not found.");
            CheckRevision(member.Revision, revision);
            if (role is null)
            {
                if (member.UserId == context.ActorId) throw new ArgumentException("You cannot remove yourself from the project.");
                if (member.RevokedAt is not null) return;
                member.RevokedAt = DateTime.UtcNow; context.Record(member, "member.removed");
            }
            else
            {
                ValidateRole(role.Value);
                if (member.RevokedAt is not null) throw new ArgumentException("Restore the member before changing their role.");
                member.Role = role.Value; context.Record(member, "member.role_changed");
            }
        });

    public Task<IReadOnlyList<AuditEvent>> GenerateKeyAsync(string projectId, int environmentId, string name, Guid operationId)
        => ExecuteAsync(projectId, operationId, ProjectRole.Admin, async context =>
        {
            await EnsureEnvironment(context, environmentId);
            var key = new ClientKey
            {
                ProjectEnvironmentId = environmentId, Name = ValidateName(string.IsNullOrWhiteSpace(name) ? "Client key" : name),
                Key = "ff_client_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).TrimEnd('=').Replace('+', '-').Replace('/', '_')
            };
            context.Db.ClientKeys.Add(key); context.Record(key, "key.created");
        });

    public Task<IReadOnlyList<AuditEvent>> RevokeKeyAsync(string projectId, int keyId, int revision, Guid operationId)
        => ExecuteAsync(projectId, operationId, ProjectRole.Admin, async context =>
        {
            var key = await context.Db.ClientKeys.SingleOrDefaultAsync(k => k.Id == keyId && k.ProjectEnvironment.ProjectId == projectId)
                ?? throw new ArgumentException("API key was not found.");
            CheckRevision(key.Revision, revision);
            if (key.RevokedAt is not null) return;
            key.RevokedAt = DateTime.UtcNow; context.Record(key, "key.revoked");
        });

    private static async Task EnsureEnvironment(MutationContext context, int environmentId)
    {
        if (!await context.Db.ProjectEnvironments.AnyAsync(e => e.Id == environmentId && e.ProjectId == context.ProjectId))
            throw new UnauthorizedAccessException("This environment does not belong to the project.");
    }
    internal static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200)
            throw new ArgumentException("Enter a name between 1 and 200 characters.");
        return name.Trim();
    }
    private static void ValidateRole(ProjectRole role)
    {
        if (!Enum.IsDefined(role)) throw new ArgumentException("Invalid project role.");
    }
    private static void CheckRevision(int saved, int draft)
    {
        if (saved != draft) throw new DbUpdateConcurrencyException("This item changed since you opened it. Reload and review the latest version before saving.");
    }
}
