using System.Security.Cryptography;
using System.Text;
using FeatureFlags.Components.Models;
using FeatureFlags.Data;
using Microsoft.EntityFrameworkCore;

namespace FeatureFlags.Services;

// A fixed operation: authorization and membership changes cannot be supplied by callers.
internal sealed class InvitationAcceptance(string token, IDbContextFactory<ApplicationDbContext> identityFactory, TimeProvider clock)
{
    internal async Task ApplyAsync(MutationContext context, CancellationToken cancellationToken)
    {
        var hash = HashToken(token);
        var invitation = await context.Db.ProjectInvitations.SingleOrDefaultAsync(i =>
            i.ProjectId == context.ProjectId && i.TokenHash == hash, cancellationToken) ?? throw InvalidInvitation();
        var now = clock.GetUtcNow().UtcDateTime;
        if (invitation.RevokedAt is not null || invitation.ExpiresAt <= now) throw InvalidInvitation();
        await using var identity = await identityFactory.CreateDbContextAsync(cancellationToken);
        var user = await identity.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == context.ActorId, cancellationToken)
            ?? throw new UnauthorizedAccessException("Sign in to accept this invitation.");
        if (string.IsNullOrEmpty(user.Email) || NormalizeEmail(user.Email) != invitation.NormalizedEmail)
            throw new UnauthorizedAccessException("Sign in with the email address this invitation was sent to.");
        var member = await context.Db.ProjectMembers.SingleOrDefaultAsync(m =>
            m.ProjectId == context.ProjectId && m.UserId == context.ActorId, cancellationToken);
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

    internal static string NormalizeEmail(string email) => email.Trim().Normalize().ToUpperInvariant();
    internal static string HashToken(string token)
    {
        if (token is null || token.Length != 64 || token.Any(c => !char.IsAsciiHexDigit(c))) throw InvalidInvitation();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
    internal static ArgumentException InvalidInvitation() => new("This invitation is invalid, expired, or no longer available.");
}
