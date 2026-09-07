using System.Security.Claims;
using FeatureFlags.Components.Models;
using FeatureFlags.Data;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;

namespace FeatureFlags.Services;

public sealed record AuditCursor(DateTime Time, long Id);
public sealed record AuditFilter(string? Type = null, string? EntityId = null, string? Actor = null,
    int? Environment = null, DateTime? FromUtc = null, DateTime? UntilUtc = null);
public sealed record AuditSummary(long Id, DateTime Time, string Actor, string Action, string EntityType,
    string EntityId, string EntityName, string? Environment);
public sealed record AuditPage(IReadOnlyList<AuditSummary> Events, AuditCursor? Next);
public sealed record HistoryOption(string Id, string Label);
public sealed record HistoryProject(string Name, IReadOnlyList<HistoryOption> Actors, IReadOnlyList<HistoryOption> Environments);

public sealed class AuditHistory(IDbContextFactory<FeatureFlagDbContext> factory, AuthenticationStateProvider authentication)
{
    private async Task Authorize(FeatureFlagDbContext db, string projectId, CancellationToken token)
    {
        var user = (await authentication.GetAuthenticationStateAsync()).User;
        var id = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (user.Identity?.IsAuthenticated != true || string.IsNullOrEmpty(id) ||
            !await db.ProjectMembers.AnyAsync(m => m.ProjectId == projectId && m.UserId == id && m.RevokedAt == null, token))
            throw new UnauthorizedAccessException("You do not have permission to view this project's history.");
    }

    public async Task<HistoryProject> GetProjectAsync(string projectId, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        await Authorize(db, projectId, token);
        var name = await db.Projects.Where(p => p.Id == projectId).Select(p => p.Name).SingleAsync(token);
        var events = db.AuditEvents.AsNoTracking().Where(e => e.ProjectId == projectId);
        var actors = await events.Select(e => new HistoryOption(e.ActorUserId, e.ActorDisplayName)).Distinct().ToListAsync(token);
        var environments = await events.Where(e => e.EnvironmentId != null)
            .Select(e => new HistoryOption(e.EnvironmentId!.Value.ToString(), e.EnvironmentName ?? "Environment")).Distinct().ToListAsync(token);
        return new(name, actors.DistinctBy(a => a.Id).OrderBy(a => a.Label).ToList(),
            environments.DistinctBy(e => e.Id).OrderBy(e => e.Label).ToList());
    }

    public async Task<AuditPage> ListAsync(string projectId, AuditFilter filter, AuditCursor? cursor = null,
        int pageSize = 50, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        await Authorize(db, projectId, token);
        if (filter.FromUtc >= filter.UntilUtc) throw new ArgumentException("The end date must be on or after the start date.");
        var query = db.AuditEvents.AsNoTracking().Where(e => e.ProjectId == projectId);
        if (!string.IsNullOrEmpty(filter.Type)) query = query.Where(e => e.EntityType == filter.Type);
        if (!string.IsNullOrEmpty(filter.EntityId)) query = query.Where(e => e.EntityId == filter.EntityId);
        if (!string.IsNullOrEmpty(filter.Actor)) query = query.Where(e => e.ActorUserId == filter.Actor);
        if (filter.Environment is not null) query = query.Where(e => e.EnvironmentId == filter.Environment);
        if (filter.FromUtc is not null) query = query.Where(e => e.OccurredAtUtc >= filter.FromUtc);
        if (filter.UntilUtc is not null) query = query.Where(e => e.OccurredAtUtc < filter.UntilUtc);
        if (cursor is not null) query = query.Where(e => e.OccurredAtUtc < cursor.Time || e.OccurredAtUtc == cursor.Time && e.Id < cursor.Id);
        var size = Math.Clamp(pageSize, 1, 100);
        var rows = await query.OrderByDescending(e => e.OccurredAtUtc).ThenByDescending(e => e.Id)
            .Select(e => new AuditSummary(e.Id, e.OccurredAtUtc, e.ActorDisplayName, e.Action, e.EntityType, e.EntityId, e.EntityName, e.EnvironmentName))
            .Take(size + 1).ToListAsync(token);
        var more = rows.Count > size;
        if (more) rows.RemoveAt(size);
        return new(rows, more ? new(rows[^1].Time, rows[^1].Id) : null);
    }

    public async Task<AuditEvent> DetailAsync(string projectId, long eventId, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        await Authorize(db, projectId, token);
        return await db.AuditEvents.AsNoTracking().SingleOrDefaultAsync(e => e.Id == eventId && e.ProjectId == projectId, token)
            ?? throw new ArgumentException("This history entry was not found in the project.");
    }

    public static string Description(string action) => action switch
    {
        "project.created" => "created project", "flag.created" => "created flag", "flag.updated" => "updated flag", "flag.deleted" => "deleted flag",
        "config.created" => "created config", "config.updated" => "updated config", "config.deleted" => "deleted config",
        "member.added" => "added member", "member.restored" => "restored member", "member.role_changed" => "changed role for",
        "member.removed" => "removed member", "key.created" => "created client key", "key.revoked" => "revoked client key", _ => action
    };
}
