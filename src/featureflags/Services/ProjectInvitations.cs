using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using static FeatureFlags.Services.InvitationAcceptance;
using FeatureFlags.Components.Models;
using FeatureFlags.Data;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;

namespace FeatureFlags.Services;

public sealed record InvitationDetails(int Id, string ProjectId, string ProjectName, string Email,
    ProjectRole Role, DateTime ExpiresAt, DateTime? AcceptedAt, DateTime? RevokedAt, string InvitedBy);
public sealed record IssuedInvitation(InvitationDetails Invitation, string? Token);

public sealed class ProjectInvitations(IDbContextFactory<FeatureFlagDbContext> factory,
    IDbContextFactory<ApplicationDbContext> identityFactory, AuthenticationStateProvider authentication, TimeProvider clock)
    : ProjectMutation(factory, authentication, clock)
{
    private readonly IDbContextFactory<FeatureFlagDbContext> invitationFactory = factory;

    public async Task<IssuedInvitation> CreateAsync(string projectId, string email, ProjectRole role, Guid operationId, CancellationToken cancellationToken = default)
    {
        email = email.Trim();
        if (email.Length > 256 || !new EmailAddressAttribute().IsValid(email))
            throw new ArgumentException("Enter a valid email address.");
        if (!Enum.IsDefined(role)) throw new ArgumentException("Invalid project role.");
        var normalized = NormalizeEmail(email);
        string? token = null;
        var events = await ExecuteAsync(projectId, operationId, ProjectRole.Admin, async context =>
        {
            await context.LockInvitationQuotaAsync(cancellationToken);
            await EnsureNotMember(context.Db, projectId, normalized, identityFactory, cancellationToken);
            if (await context.Db.ProjectInvitations.AnyAsync(i => i.ProjectId == projectId &&
                i.NormalizedEmail == normalized && i.AcceptedAt == null && i.RevokedAt == null, cancellationToken))
                throw new ArgumentException("An invitation already exists for this email. Use Resend or Revoke.");
            var now = Clock.GetUtcNow().UtcDateTime;
            await CheckSendLimit(context.Db, projectId, now, cancellationToken);
            token = NewToken();
            var invitation = new ProjectInvitation
            {
                ProjectId = projectId, Email = email, NormalizedEmail = normalized, Role = role,
                InvitedByUserId = context.ActorId, TokenHash = HashToken(token),
                CreatedAt = now, IssuedAt = now, ExpiresAt = now.AddDays(7)
            };
            context.Db.ProjectInvitations.Add(invitation);
            context.Record(invitation, "invitation.created");
        }, cancellationToken: cancellationToken);
        return new(await DetailsByIdAsync(projectId, int.Parse(events.Single(e => e.EntityType == "invitation").EntityId)), token);
    }

    public async Task<IssuedInvitation> RenewAsync(string projectId, int id, int revision, Guid operationId, CancellationToken cancellationToken = default)
    {
        string? token = null;
        await ExecuteAsync(projectId, operationId, ProjectRole.Admin, async context =>
        {
            await context.LockInvitationQuotaAsync(cancellationToken);
            var invitation = await FindPending(context.Db, projectId, id, revision, cancellationToken);
            await EnsureNotMember(context.Db, projectId, invitation.NormalizedEmail, identityFactory, cancellationToken);
            var now = Clock.GetUtcNow().UtcDateTime;
            if (now < invitation.IssuedAt.AddMinutes(1)) throw new ArgumentException("Wait one minute before resending this invitation.");
            await CheckSendLimit(context.Db, projectId, now, cancellationToken);
            token = NewToken();
            invitation.TokenHash = HashToken(token);
            invitation.IssuedAt = now;
            invitation.ExpiresAt = now.AddDays(7);
            context.Record(invitation, "invitation.renewed");
        }, cancellationToken: cancellationToken);
        return new(await DetailsByIdAsync(projectId, id), token);
    }

    public Task<IReadOnlyList<AuditEvent>> RevokeAsync(string projectId, int id, int revision, Guid operationId, CancellationToken cancellationToken = default)
        => ExecuteAsync(projectId, operationId, ProjectRole.Admin, async context =>
        {
            var invitation = await FindPending(context.Db, projectId, id, revision, cancellationToken);
            invitation.RevokedAt = Clock.GetUtcNow().UtcDateTime;
            context.Record(invitation, "invitation.revoked");
        }, cancellationToken: cancellationToken);

    // A valid email link permits previewing this invitation only, never reading project resources.
    public async Task<InvitationDetails> PreviewAsync(string token, CancellationToken cancellationToken = default)
    {
        var hash = HashToken(token);
        await using var db = await invitationFactory.CreateDbContextAsync(cancellationToken);
        var invitation = await db.ProjectInvitations.AsNoTracking().Include(i => i.Project)
            .SingleOrDefaultAsync(i => i.TokenHash == hash, cancellationToken) ?? throw InvalidInvitation();
        if (invitation.RevokedAt is not null || invitation.ExpiresAt <= Clock.GetUtcNow().UtcDateTime)
            throw InvalidInvitation();
        return await DetailsAsync(db, invitation, cancellationToken);
    }

    public async Task<string> AcceptAsync(string token, Guid operationId, CancellationToken cancellationToken = default)
    {
        var details = await PreviewAsync(token, cancellationToken);
        try { await ExecuteInvitationAcceptanceAsync(details.ProjectId, operationId, new InvitationAcceptance(token, identityFactory, Clock), cancellationToken); }
        catch (DbUpdateException ex) when (ex is DbUpdateConcurrencyException ||
            ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            // A competing acceptance may have committed first. Revalidate the token, user and membership in a fresh transaction.
            await ExecuteInvitationAcceptanceAsync(details.ProjectId, operationId, new InvitationAcceptance(token, identityFactory, Clock), cancellationToken);
        }
        return details.ProjectId;
    }

    private async Task<InvitationDetails> DetailsByIdAsync(string projectId, int id, CancellationToken cancellationToken = default)
    {
        await using var db = await invitationFactory.CreateDbContextAsync(cancellationToken);
        return await DetailsAsync(db, await db.ProjectInvitations.AsNoTracking().Include(i => i.Project).SingleAsync(i => i.Id == id && i.ProjectId == projectId, cancellationToken), cancellationToken);
    }
    private static async Task<InvitationDetails> DetailsAsync(FeatureFlagDbContext db, ProjectInvitation i, CancellationToken cancellationToken)
    {
        var inviter = await db.ProjectMembers.Where(m => m.ProjectId == i.ProjectId && m.UserId == i.InvitedByUserId)
            .Select(m => m.Email).FirstOrDefaultAsync(cancellationToken);
        return new(i.Id, i.ProjectId, i.Project.Name, i.Email, i.Role, i.ExpiresAt, i.AcceptedAt, i.RevokedAt,
            string.IsNullOrWhiteSpace(inviter) ? "A project administrator" : inviter);
    }
    private static async Task<ProjectInvitation> FindPending(FeatureFlagDbContext db, string projectId, int id, int revision, CancellationToken cancellationToken)
    {
        var invitation = await db.ProjectInvitations.SingleOrDefaultAsync(i => i.Id == id && i.ProjectId == projectId, cancellationToken)
            ?? throw InvalidInvitation();
        if (invitation.Revision != revision) throw new DbUpdateConcurrencyException("The invitation changed. Refresh and try again.");
        if (invitation.AcceptedAt is not null || invitation.RevokedAt is not null) throw InvalidInvitation();
        return invitation;
    }
    private static async Task EnsureNotMember(FeatureFlagDbContext db, string projectId, string email,
        IDbContextFactory<ApplicationDbContext> identityFactory, CancellationToken cancellationToken)
    {
        await using var identity = await identityFactory.CreateDbContextAsync(cancellationToken);
        var ids = await identity.Users.Where(u => u.NormalizedEmail == email).Select(u => u.Id).ToListAsync(cancellationToken);
        if (await db.ProjectMembers.AnyAsync(m => m.ProjectId == projectId && ids.Contains(m.UserId) && m.RevokedAt == null, cancellationToken))
            throw new ArgumentException("That user is already a member of this project.");
    }
    private static async Task CheckSendLimit(FeatureFlagDbContext db, string projectId, DateTime now, CancellationToken cancellationToken)
    {
        if (await db.AuditEvents.CountAsync(e => e.ProjectId == projectId && e.OccurredAtUtc > now.AddHours(-1) &&
            (e.Action == "invitation.created" || e.Action == "invitation.renewed"), cancellationToken) >= 20)
            throw new ArgumentException("This project has sent too many invitations. Try again later.");
    }
    public static string NormalizeEmail(string email) => InvitationAcceptance.NormalizeEmail(email);
    private static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
}
