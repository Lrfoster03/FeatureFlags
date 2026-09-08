using System.Security.Claims;
using Bunit.TestDoubles;
using FeatureFlags.Components.Models;
using FeatureFlags.Data;
using FeatureFlags.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace FeatureFlags.Tests;

public class ProjectInvitationTests
{
    [Fact]
    public async Task Unregistered_recipient_can_join_after_signup_with_one_atomic_audit_operation()
    {
        await using var f = await InvitationFixture.CreateAsync();
        var issued = await f.As("owner").CreateAsync(f.ProjectId, "  New@Example.com  ", ProjectRole.Editor, Guid.NewGuid());
        await using var db = f.Factory.CreateDbContext();
        Assert.Single(await db.ProjectMembers.ToListAsync());
        var invitation = await db.ProjectInvitations.AsNoTracking().SingleAsync();
        Assert.NotEqual(issued.Token, invitation.TokenHash);
        var creation = await db.AuditEvents.SingleAsync();
        Assert.DoesNotContain(issued.Token!, creation.After!);
        Assert.DoesNotContain(invitation.TokenHash, creation.After!);
        await f.AddUser("new", "new@example.com");
        var operation = Guid.NewGuid();
        Assert.Equal(f.ProjectId, await f.As("new").AcceptAsync(issued.Token!, operation));
        await f.As("new").AcceptAsync(issued.Token!, operation);
        await f.As("new").AcceptAsync(issued.Token!, Guid.NewGuid());
        var member = await db.ProjectMembers.AsNoTracking().SingleAsync(m => m.UserId == "new");
        Assert.Equal(ProjectRole.Editor, member.Role);
        Assert.NotNull((await db.ProjectInvitations.AsNoTracking().SingleAsync()).AcceptedAt);
        var accepted = await db.AuditEvents.Where(e => e.OperationId == operation).ToListAsync();
        Assert.Equal(2, accepted.Count);
        Assert.Contains(accepted, e => e.Action == "invitation.accepted" && e.ActorUserId == "new");
        Assert.Contains(accepted, e => e.Action == "member.added");
        Assert.Equal(3, await db.AuditEvents.CountAsync());
    }

    [Fact]
    public async Task Wrong_account_expired_revoked_and_replaced_tokens_cannot_join()
    {
        await using var f = await InvitationFixture.CreateAsync();
        await f.AddUser("recipient", "recipient@example.com");
        await f.AddUser("wrong", "wrong@example.com");
        var owner = f.As("owner");
        var issued = await owner.CreateAsync(f.ProjectId, "recipient@example.com", ProjectRole.Viewer, Guid.NewGuid());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.As("wrong").AcceptAsync(issued.Token!, Guid.NewGuid()));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.As("wrong").CreateAsync(f.ProjectId, "other@example.com", ProjectRole.Owner, Guid.NewGuid()));
        await Assert.ThrowsAsync<ArgumentException>(() => owner.RenewAsync(f.ProjectId, issued.Invitation.Id, 1, Guid.NewGuid()));
        f.Clock.Now = f.Clock.Now.AddDays(8);
        await Assert.ThrowsAsync<ArgumentException>(() => f.As("recipient").AcceptAsync(issued.Token!, Guid.NewGuid()));
        var renewed = await owner.RenewAsync(f.ProjectId, issued.Invitation.Id, 1, Guid.NewGuid());
        await Assert.ThrowsAsync<ArgumentException>(() => f.As("recipient").AcceptAsync(issued.Token!, Guid.NewGuid()));
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => owner.RevokeAsync(f.ProjectId, issued.Invitation.Id, 1, Guid.NewGuid()));
        await owner.RevokeAsync(f.ProjectId, issued.Invitation.Id, 2, Guid.NewGuid());
        await Assert.ThrowsAsync<ArgumentException>(() => f.As("recipient").AcceptAsync(renewed.Token!, Guid.NewGuid()));
        await using var db = f.Factory.CreateDbContext();
        Assert.Single(await db.ProjectMembers.ToListAsync());
    }

    [Fact]
    public async Task Duplicate_creation_retry_does_not_return_an_unpersisted_token_or_create_another_invitation()
    {
        await using var f = await InvitationFixture.CreateAsync();
        var operation = Guid.NewGuid();
        var first = await f.As("owner").CreateAsync(f.ProjectId, "new@example.com", ProjectRole.Viewer, operation);
        var retry = await f.As("owner").CreateAsync(f.ProjectId, "new@example.com", ProjectRole.Viewer, operation);
        Assert.Equal(first.Invitation.Id, retry.Invitation.Id);
        Assert.Null(retry.Token);
        await Assert.ThrowsAsync<ArgumentException>(() => f.As("owner").CreateAsync(f.ProjectId, "NEW@example.com", ProjectRole.Viewer, Guid.NewGuid()));
        await Assert.ThrowsAsync<ArgumentException>(() => f.As("owner").CreateAsync(f.ProjectId, "owner@example.com", ProjectRole.Viewer, Guid.NewGuid()));
        await Assert.ThrowsAsync<ArgumentException>(() => f.As("owner").CreateAsync(f.ProjectId, "bad", ProjectRole.Viewer, Guid.NewGuid()));
        await using var db = f.Factory.CreateDbContext();
        Assert.Single(await db.ProjectInvitations.ToListAsync());
    }

    [Fact]
    public async Task Acceptance_restores_removed_members_but_cannot_reuse_an_accepted_invite_after_removal()
    {
        await using var f = await InvitationFixture.CreateAsync();
        await f.AddUser("recipient", "recipient@example.com");
        await using var db = f.Factory.CreateDbContext();
        var member = new ProjectMember { ProjectId = f.ProjectId, UserId = "recipient", Email = "recipient@example.com", RevokedAt = f.Clock.Now.UtcDateTime };
        db.ProjectMembers.Add(member); await db.SaveSeedChangesAsync();
        var invitation = await f.As("owner").CreateAsync(f.ProjectId, member.Email, ProjectRole.Editor, Guid.NewGuid());
        await f.As("recipient").AcceptAsync(invitation.Token!, Guid.NewGuid());
        db.ChangeTracker.Clear();
        member = await db.ProjectMembers.SingleAsync(m => m.UserId == "recipient");
        Assert.Null(member.RevokedAt);
        Assert.Equal(ProjectRole.Editor, member.Role);
        Assert.Contains(await db.AuditEvents.ToListAsync(), e => e.Action == "member.restored");
        member.RevokedAt = f.Clock.Now.UtcDateTime; await db.SaveSeedChangesAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => f.As("recipient").AcceptAsync(invitation.Token!, Guid.NewGuid()));
    }

    [Fact]
    public async Task Audit_failure_rolls_back_both_acceptance_and_membership()
    {
        await using var f = await InvitationFixture.CreateAsync();
        await f.AddUser("recipient", "recipient@example.com");
        var issued = await f.As("owner").CreateAsync(f.ProjectId, "recipient@example.com", ProjectRole.Viewer, Guid.NewGuid());
        var factory = new TestContextFactory(() => new RejectInvitationAudit(f.Options));
        var failing = new ProjectInvitations(factory, f.IdentityFactory, InvitationFixture.Auth("recipient"), f.Clock);
        await Assert.ThrowsAsync<DbUpdateException>(() => failing.AcceptAsync(issued.Token!, Guid.NewGuid()));
        await using var db = f.Factory.CreateDbContext();
        Assert.Null((await db.ProjectInvitations.SingleAsync()).AcceptedAt);
        Assert.Single(await db.ProjectMembers.ToListAsync());
        Assert.Single(await db.AuditEvents.ToListAsync());
    }

    [Fact]
    public async Task PostgreSql_concurrent_acceptance_with_different_operations_creates_one_membership()
    {
        var connectionString = Environment.GetEnvironmentVariable("AUDIT_TEST_POSTGRES");
        if (string.IsNullOrEmpty(connectionString)) return; // CI supplies a disposable PostgreSQL database.
        var schema = "invitation_test_" + Guid.NewGuid().ToString("N");
        await using var connection = new Npgsql.NpgsqlConnection(connectionString); await connection.OpenAsync();
        await using (var command = new Npgsql.NpgsqlCommand($"CREATE SCHEMA {schema}", connection)) await command.ExecuteNonQueryAsync();
        try
        {
            var options = new DbContextOptionsBuilder<FeatureFlagDbContext>().UseNpgsql(connectionString + ";Search Path=" + schema).Options;
            using var identityServices = new ServiceCollection().Configure<IdentityOptions>(o => o.Stores.MaxLengthForKeys = 128).BuildServiceProvider();
            var identityOptions = new DbContextOptionsBuilder<ApplicationDbContext>().UseApplicationServiceProvider(identityServices).UseNpgsql(connectionString + ";Search Path=" + schema).Options;
            var factory = new TestContextFactory(() => new FeatureFlagDbContext(options));
            var identities = new PostgresIdentities(identityOptions);
            await using var db = factory.CreateDbContext(); await db.Database.MigrateAsync();
            await using var identity = identities.CreateDbContext(); await identity.Database.MigrateAsync();
            identity.Users.AddRange(new ApplicationUser { Id = "owner", Email = "owner@example.com", NormalizedEmail = "OWNER@EXAMPLE.COM" },
                new ApplicationUser { Id = "recipient", Email = "recipient@example.com", NormalizedEmail = "RECIPIENT@EXAMPLE.COM" });
            await identity.SaveChangesAsync();
            var project = new Project { Name = "Concurrent invitations", Members = { new ProjectMember { UserId = "owner", Role = ProjectRole.Owner } } };
            db.Projects.Add(project); await db.SaveSeedChangesAsync();
            var owner = new ProjectInvitations(factory, identities, InvitationFixture.Auth("owner"), TimeProvider.System);
            var issued = await owner.CreateAsync(project.Id, "recipient@example.com", ProjectRole.Editor, Guid.NewGuid());
            var reached = 0;
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task WaitForBoth()
            {
                if (Interlocked.Increment(ref reached) == 2) ready.TrySetResult();
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
            }
            var racing = new TestContextFactory(() => new PauseAcceptanceContext(options, WaitForBoth));
            var recipient = new ProjectInvitations(racing, identities, InvitationFixture.Auth("recipient"), TimeProvider.System);
            await Task.WhenAll(recipient.AcceptAsync(issued.Token!, Guid.NewGuid()), recipient.AcceptAsync(issued.Token!, Guid.NewGuid()));
            Assert.Single(await db.ProjectMembers.Where(m => m.UserId == "recipient").ToListAsync());
            Assert.Equal(1, await db.AuditEvents.CountAsync(e => e.Action == "invitation.accepted"));
            Assert.Equal(1, await db.AuditEvents.CountAsync(e => e.Action == "member.added"));
        }
        finally { await using var drop = new Npgsql.NpgsqlCommand($"DROP SCHEMA {schema} CASCADE", connection); await drop.ExecuteNonQueryAsync(); }
    }
    private sealed class PostgresIdentities(DbContextOptions<ApplicationDbContext> options) : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
    }
    private sealed class PauseAcceptanceContext(DbContextOptions<FeatureFlagDbContext> options, Func<Task> wait) : FeatureFlagDbContext(options)
    {
        public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            if (ChangeTracker.Entries<ProjectInvitation>().Any(e => e.State == EntityState.Modified && e.Entity.AcceptedAt != null)) await wait();
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
    }

    private sealed class RejectInvitationAudit(DbContextOptions<FeatureFlagDbContext> options) : FeatureFlagDbContext(options)
    {
        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            if (ChangeTracker.Entries<AuditEvent>().Any(e => e.State == EntityState.Added)) throw new DbUpdateException("Test audit failure");
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
    }
}

internal sealed class InvitationFixture : IAsyncDisposable
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly SqliteConnection identityConnection = new("Data Source=:memory:");
    public TestClock Clock { get; } = new();
    public DbContextOptions<FeatureFlagDbContext> Options { get; private set; } = default!;
    public TestContextFactory Factory { get; private set; } = default!;
    public IDbContextFactory<ApplicationDbContext> IdentityFactory { get; private set; } = default!;
    public string ProjectId { get; private set; } = "";
    public static AuthenticationStateProvider Auth(string id) => new BunitAuthenticationStateProvider(id + "@example.com", [],
        [new Claim(ClaimTypes.NameIdentifier, id)], "Test");
    public ProjectInvitations As(string id) => new(Factory, IdentityFactory, Auth(id), Clock);
    public static async Task<InvitationFixture> CreateAsync()
    {
        var f = new InvitationFixture();
        await f.connection.OpenAsync(); await f.identityConnection.OpenAsync();
        f.Options = new DbContextOptionsBuilder<FeatureFlagDbContext>().UseSqlite(f.connection).Options;
        f.Factory = new TestContextFactory(() => new FeatureFlagDbContext(f.Options));
        f.IdentityFactory = new IdentityContexts(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(f.identityConnection).Options);
        await using var db = f.Factory.CreateDbContext(); await db.Database.EnsureCreatedAsync();
        await using var identity = f.IdentityFactory.CreateDbContext(); await identity.Database.EnsureCreatedAsync();
        await f.AddUser("owner", "owner@example.com");
        var project = new Project { Name = "Invitation project", Members = { new ProjectMember
            { UserId = "owner", Email = "owner@example.com", Role = ProjectRole.Owner } } };
        db.Projects.Add(project); await db.SaveSeedChangesAsync(); f.ProjectId = project.Id;
        return f;
    }
    public async Task AddUser(string id, string email)
    {
        await using var db = IdentityFactory.CreateDbContext();
        db.Users.Add(new ApplicationUser { Id = id, Email = email, NormalizedEmail = ProjectInvitations.NormalizeEmail(email), UserName = email });
        await db.SaveChangesAsync();
    }
    public async ValueTask DisposeAsync() { await connection.DisposeAsync(); await identityConnection.DisposeAsync(); }
    private sealed class IdentityContexts(DbContextOptions<ApplicationDbContext> options) : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
    }
    internal sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
