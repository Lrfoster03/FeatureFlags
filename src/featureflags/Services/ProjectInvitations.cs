using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
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
    : ProjectMutation(factory, authentication)
{
    private readonly IDbContextFactory<FeatureFlagDbContext> invitationFactory = factory;

    public async Task<IssuedInvitation> CreateAsync(string projectId, string email, ProjectRole role, Guid operationId)
    {
        email = email.Trim();
        if (email.Length > 256 || !new EmailAddressAttribute().IsValid(email))
            throw new ArgumentException("Enter a valid email address.");
        if (!Enum.IsDefined(role)) throw new ArgumentException("Invalid project role.");
        var normalized = NormalizeEmail(email);
        string? token = null;
        var events = await ExecuteAsync(projectId, operationId, ProjectRole.Admin, async context =>
        {
            await EnsureNotMember(context.Db, projectId, normalized, identityFactory);
            if (await context.Db.ProjectInvitations.AnyAsync(i => i.ProjectId == projectId &&
                i.NormalizedEmail == normalized && i.AcceptedAt == null && i.RevokedAt == null))
                throw new ArgumentException("An invitation already exists for this email. Use Resend or Revoke.");
            var now = clock.GetUtcNow().UtcDateTime;
            await CheckSendLimit(context.Db, projectId, now);
            token = NewToken();
            var invitation = new ProjectInvitation
            {
                ProjectId = projectId, Email = email, NormalizedEmail = normalized, Role = role,
                InvitedByUserId = context.ActorId, TokenHash = HashToken(token),
                CreatedAt = now, IssuedAt = now, ExpiresAt = now.AddDays(7)
            };
            context.Db.ProjectInvitations.Add(invitation);
            context.Record(invitation, "invitation.created");
        });
        return new(await DetailsByIdAsync(projectId, int.Parse(events.Single(e => e.EntityType == "invitation").EntityId)), token);
    }

    public async Task<IssuedInvitation> RenewAsync(string projectId, int id, int revision, Guid operationId)
    {
        string? token = null;
        await ExecuteAsync(projectId, operationId, ProjectRole.Admin, async context =>
        {
            var invitation = await FindPending(context.Db, projectId, id, revision);
            await EnsureNotMember(context.Db, projectId, invitation.NormalizedEmail, identityFactory);
            var now = clock.GetUtcNow().UtcDateTime;
            if (now < invitation.IssuedAt.AddMinutes(1)) throw new ArgumentException("Wait one minute before resending this invitation.");
            await CheckSendLimit(context.Db, projectId, now);
            token = NewToken();
            invitation.TokenHash = HashToken(token);
            invitation.IssuedAt = now;
            invitation.ExpiresAt = now.AddDays(7);
            context.Record(invitation, "invitation.renewed");
        });
        return new(await DetailsByIdAsync(projectId, id), token);
    }

    public Task<IReadOnlyList<AuditEvent>> RevokeAsync(string projectId, int id, int revision, Guid operationId)
        => ExecuteAsync(projectId, operationId, ProjectRole.Admin, async context =>
        {
            var invitation = await FindPending(context.Db, projectId, id, revision);
            invitation.RevokedAt = clock.GetUtcNow().UtcDateTime;
            context.Record(invitation, "invitation.revoked");
        });

    // A valid email link permits previewing this invitation only, never reading project resources.
    public async Task<InvitationDetails> PreviewAsync(string token)
    {
        var hash = HashToken(token);
        await using var db = await invitationFactory.CreateDbContextAsync();
        var invitation = await db.ProjectInvitations.AsNoTracking().Include(i => i.Project)
            .SingleOrDefaultAsync(i => i.TokenHash == hash) ?? throw InvalidInvitation();
        if (invitation.RevokedAt is not null || invitation.ExpiresAt <= clock.GetUtcNow().UtcDateTime)
            throw InvalidInvitation();
        return await DetailsAsync(db, invitation);
    }

    public async Task<string> AcceptAsync(string token, Guid operationId)
    {
        var details = await PreviewAsync(token);
        try { await ExecuteInvitationAcceptanceAsync(details.ProjectId, operationId, token, identityFactory, clock); }
        catch (DbUpdateException ex) when (ex is DbUpdateConcurrencyException ||
            ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            // A competing acceptance may have committed first. Revalidate the token, user and membership in a fresh transaction.
            await ExecuteInvitationAcceptanceAsync(details.ProjectId, operationId, token, identityFactory, clock);
        }
        return details.ProjectId;
    }

    internal static async Task ApplyAcceptanceAsync(MutationContext context, string token,
        IDbContextFactory<ApplicationDbContext> identityFactory, TimeProvider clock)
    {
        var hash = HashToken(token);
        var invitation = await context.Db.ProjectInvitations.SingleOrDefaultAsync(i =>
            i.ProjectId == context.ProjectId && i.TokenHash == hash) ?? throw InvalidInvitation();
        var now = clock.GetUtcNow().UtcDateTime;
        if (invitation.RevokedAt is not null || invitation.ExpiresAt <= now) throw InvalidInvitation();
        await using var identity = await identityFactory.CreateDbContextAsync();
        var user = await identity.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == context.ActorId)
            ?? throw new UnauthorizedAccessException("Sign in to accept this invitation.");
        if (string.IsNullOrEmpty(user.Email) || NormalizeEmail(user.Email) != invitation.NormalizedEmail)
            throw new UnauthorizedAccessException("Sign in with the email address this invitation was sent to.");
        var member = await context.Db.ProjectMembers.SingleOrDefaultAsync(m =>
            m.ProjectId == context.ProjectId && m.UserId == context.ActorId);
        if (invitation.AcceptedAt is not null)
        {
            if (invitation.AcceptedByUserId == user.Id && member is { RevokedAt: null }) return;
            throw InvalidInvitation();
        }
        if (member is null || member.RevokedAt is not null)
        {
            var restoring = member is not null;
            if (member is null)
            {
                member = new ProjectMember { ProjectId = context.ProjectId, UserId = user.Id };
                context.Db.ProjectMembers.Add(member);
            }
            member.Email = user.Email;
            member.DisplayName = user.UserName ?? user.Email;
            member.Role = invitation.Role;
            member.RevokedAt = null;
            context.Record(member, restoring ? "member.restored" : "member.added");
        }
        invitation.AcceptedAt = now;
        invitation.AcceptedByUserId = user.Id;
        context.Record(invitation, "invitation.accepted");
    }

    private async Task<InvitationDetails> DetailsByIdAsync(string projectId, int id)
    {
        await using var db = await invitationFactory.CreateDbContextAsync();
        return await DetailsAsync(db, await db.ProjectInvitations.AsNoTracking().Include(i => i.Project).SingleAsync(i => i.Id == id && i.ProjectId == projectId));
    }
    private static async Task<InvitationDetails> DetailsAsync(FeatureFlagDbContext db, ProjectInvitation i)
    {
        var inviter = await db.ProjectMembers.Where(m => m.ProjectId == i.ProjectId && m.UserId == i.InvitedByUserId)
            .Select(m => m.Email).FirstOrDefaultAsync();
        return new(i.Id, i.ProjectId, i.Project.Name, i.Email, i.Role, i.ExpiresAt, i.AcceptedAt, i.RevokedAt,
            string.IsNullOrWhiteSpace(inviter) ? "A project administrator" : inviter);
    }
    private static async Task<ProjectInvitation> FindPending(FeatureFlagDbContext db, string projectId, int id, int revision)
    {
        var invitation = await db.ProjectInvitations.SingleOrDefaultAsync(i => i.Id == id && i.ProjectId == projectId)
            ?? throw InvalidInvitation();
        if (invitation.Revision != revision) throw new DbUpdateConcurrencyException("The invitation changed. Refresh and try again.");
        if (invitation.AcceptedAt is not null || invitation.RevokedAt is not null) throw InvalidInvitation();
        return invitation;
    }
    private static async Task EnsureNotMember(FeatureFlagDbContext db, string projectId, string email,
        IDbContextFactory<ApplicationDbContext> identityFactory)
    {
        await using var identity = await identityFactory.CreateDbContextAsync();
        var ids = await identity.Users.Where(u => u.NormalizedEmail == email).Select(u => u.Id).ToListAsync();
        if (await db.ProjectMembers.AnyAsync(m => m.ProjectId == projectId && ids.Contains(m.UserId) && m.RevokedAt == null))
            throw new ArgumentException("That user is already a member of this project.");
    }
    private static async Task CheckSendLimit(FeatureFlagDbContext db, string projectId, DateTime now)
    {
        if (await db.AuditEvents.CountAsync(e => e.ProjectId == projectId && e.OccurredAtUtc > now.AddHours(-1) &&
            (e.Action == "invitation.created" || e.Action == "invitation.renewed")) >= 20)
            throw new ArgumentException("This project has sent too many invitations. Try again later.");
    }
    public static string NormalizeEmail(string email) => email.Trim().Normalize().ToUpperInvariant();
    private static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    internal static string HashToken(string token)
    {
        if (token is null || token.Length != 64 || token.Any(c => !char.IsAsciiHexDigit(c))) throw InvalidInvitation();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
    private static ArgumentException InvalidInvitation() => new("This invitation is invalid, expired, or no longer available.");
}
