using System.Security.Claims;
using System.Text.Json.Nodes;
using Bunit.TestDoubles;
using FeatureFlags.Components.Models;
using FeatureFlags.Data;
using FeatureFlags.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FeatureFlags.Tests;

public class ProjectMutationTests
{
    [Fact]
    public async Task Save_records_persisted_before_after_and_rejects_stale_edits()
    {
        await using var fixture = await Fixture.CreateAsync();
        var draft = await fixture.FlagAsync();
        draft.PercentageRollout = 25;
        var operation = Guid.NewGuid();
        var events = await fixture.Changes.SaveItemsAsync(fixture.Project.Id, [draft], operation);
        var audit = Assert.Single(events);
        Assert.Equal(10, JsonNode.Parse(audit.Before!)!["percentageRollout"]!.GetValue<int>());
        Assert.Equal(25, JsonNode.Parse(audit.After!)!["percentageRollout"]!.GetValue<int>());
        Assert.Equal("Owner", audit.ActorDisplayName);
        Assert.Equal(fixture.EnvironmentId, audit.EnvironmentId);
        Assert.Equal("flag.updated", audit.Action);
        Assert.Equal(audit.Id, Assert.Single(await fixture.Changes.SaveItemsAsync(fixture.Project.Id, [draft], operation)).Id);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => fixture.Changes.SaveItemsAsync(fixture.Project.Id, [draft], Guid.NewGuid()));
        var latest = await fixture.FlagAsync();
        Assert.Equal(2, latest.Revision);
        Assert.Empty(await fixture.Changes.SaveItemsAsync(fixture.Project.Id, [latest], Guid.NewGuid()));
        await using var db = fixture.Factory.CreateDbContext();
        Assert.Single(await db.AuditEvents.ToListAsync());
    }

    [Fact]
    public async Task Config_schema_and_value_changes_share_one_event_and_formatting_is_ignored()
    {
        await using var f = await Fixture.CreateAsync();
        await using var db = f.Factory.CreateDbContext();
        var config = new FeatureConfig { Name = "Checkout", ProjectEnvironmentId = f.EnvironmentId,
            Value = JsonNode.Parse("{\"a\":1,\"b\":true}")!.AsObject() };
        db.Configs.Add(config); await db.SaveSeedChangesAsync();
        config.Value = JsonNode.Parse("{\"b\":true, \"a\":1}")!.AsObject();
        Assert.Empty(await f.Changes.SaveItemsAsync(f.Project.Id, [config], Guid.NewGuid()));
        config.Value["new"] = null;
        config.Schema = JsonNode.Parse("{\"type\":\"object\"}")!.AsObject();
        var audit = Assert.Single(await f.Changes.SaveItemsAsync(f.Project.Id, [config], Guid.NewGuid()));
        Assert.False(JsonNode.Parse(audit.Before!)!["value"]!.AsObject().ContainsKey("new"));
        Assert.True(JsonNode.Parse(audit.After!)!["value"]!.AsObject().ContainsKey("new"));
        Assert.Equal("object", JsonNode.Parse(audit.After!)!["schema"]!["type"]!.GetValue<string>());
    }

    [Fact]
    public async Task Failure_to_insert_audit_rolls_back_resource_change()
    {
        await using var f = await Fixture.CreateAsync();
        var changes = new ProjectChanges(new TestContextFactory(() => new RejectAuditContext(f.Options)), new UnusedIdentityFactory(), f.Auth);
        var draft = await f.FlagAsync(); draft.PercentageRollout = 80;
        await Assert.ThrowsAsync<DbUpdateException>(() => changes.SaveItemsAsync(f.Project.Id, [draft], Guid.NewGuid()));
        Assert.Equal(10, (await f.FlagAsync()).PercentageRollout);
        await using var db = f.Factory.CreateDbContext(); Assert.Empty(await db.AuditEvents.ToListAsync());
    }

    [Fact]
    public async Task Guard_rejects_direct_saves_uncovered_batch_items_and_audit_rewrites()
    {
        await using var f = await Fixture.CreateAsync();
        await using (var db = f.Factory.CreateDbContext())
        {
            db.FeatureFlags.Add(new FeatureFlag { Name = "Bypass", ProjectEnvironmentId = f.EnvironmentId });
            Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }
        var mutation = new TestMutation(f.Factory, f.Auth);
        await Assert.ThrowsAsync<InvalidOperationException>(() => mutation.Run(f.Project.Id, async c =>
        {
            var flag = await c.Db.FeatureFlags.SingleAsync(); flag.Name = "Changed"; c.Record(flag, "flag.updated");
            c.Db.Configs.Add(new FeatureConfig { Name = "Missing audit", ProjectEnvironmentId = f.EnvironmentId });
        }));
        await using var verify = f.Factory.CreateDbContext();
        Assert.Equal("Alpha", (await verify.FeatureFlags.SingleAsync()).Name);
        Assert.Empty(await verify.Configs.ToListAsync());
        var draft = await f.FlagAsync(); draft.Name = "Recorded";
        await f.Changes.SaveItemsAsync(f.Project.Id, [draft], Guid.NewGuid());
        var audit = await verify.AuditEvents.SingleAsync(); audit.EntityName = "Rewritten";
        await Assert.ThrowsAsync<InvalidOperationException>(() => verify.SaveSeedChangesAsync());
    }

    [Fact]
    public async Task Revoked_users_and_foreign_resources_cannot_be_changed()
    {
        await using var f = await Fixture.CreateAsync();
        var foreign = new FeatureFlag { Id = 99999, Name = "Foreign", Revision = 1 };
        await Assert.ThrowsAsync<ArgumentException>(() => f.Changes.DeleteItemAsync(f.Project.Id, foreign, Guid.NewGuid()));
        await using var db = f.Factory.CreateDbContext();
        var owner = await db.ProjectMembers.SingleAsync(); owner.RevokedAt = DateTime.UtcNow; await db.SaveSeedChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Changes.GenerateKeyAsync(f.Project.Id, f.EnvironmentId, "Denied", Guid.NewGuid()));
        Assert.Empty(await db.AuditEvents.ToListAsync());
    }

    [Fact]
    public async Task New_project_covers_initial_environment_and_owner_and_keys_never_leak()
    {
        await using var f = await Fixture.CreateAsync();
        var service = new ProjectProvisioningService(f.Factory, f.Auth);
        var project = await service.CreateProjectForUserAsync(new ApplicationUser { Id = "owner", UserName = "owner@example.com", Email = "owner@example.com" }, "New project");
        await using var db = f.Factory.CreateDbContext();
        var created = await db.AuditEvents.SingleAsync(); Assert.Equal("project.created", created.Action);
        Assert.Equal(project.Id, created.ProjectId);
        Assert.Contains("Development", created.After);
        var keyEvent = Assert.Single(await f.Changes.GenerateKeyAsync(f.Project.Id, f.EnvironmentId, "Browser", Guid.NewGuid()));
        var key = await db.ClientKeys.SingleAsync();
        Assert.DoesNotContain(key.Key, keyEvent.After!);
        Assert.DoesNotContain("ff_client_", keyEvent.After!);
        var revoked = Assert.Single(await f.Changes.RevokeKeyAsync(f.Project.Id, key.Id, key.Revision, Guid.NewGuid()));
        Assert.Equal("key.revoked", revoked.Action);
    }

    [Fact]
    public async Task PostgreSql_migration_jsonb_and_rollback()
    {
        var connectionString = Environment.GetEnvironmentVariable("AUDIT_TEST_POSTGRES");
        if (string.IsNullOrEmpty(connectionString)) return; // CI supplies a disposable PostgreSQL database.
        var schema = "audit_test_" + Guid.NewGuid().ToString("N");
        await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA {schema}", connection)) await create.ExecuteNonQueryAsync();
        try
        {
            var options = new DbContextOptionsBuilder<FeatureFlagDbContext>().UseNpgsql(connectionString + ";Search Path=" + schema).Options;
            await using var db = new FeatureFlagDbContext(options); await db.Database.MigrateAsync();
            Assert.Equal("jsonb", db.Model.FindEntityType(typeof(AuditEvent))!.FindProperty(nameof(AuditEvent.After))!.GetColumnType());
            var factory = new TestContextFactory(() => new FeatureFlagDbContext(options));
            var auth = Fixture.Authentication();
            var project = await new ProjectProvisioningService(factory, auth).CreateProjectForUserAsync(new ApplicationUser { Id = "owner", Email = "owner@example.com" }, "Postgres");
            var changes = new ProjectChanges(factory, new UnusedIdentityFactory(), auth);
            await changes.AddItemAsync(project.Id, project.Environments.Single().Id, false, Guid.NewGuid());
            var draft = await db.FeatureFlags.AsNoTracking().SingleAsync(); draft.PercentageRollout = 55;
            await changes.SaveItemsAsync(project.Id, [draft], Guid.NewGuid());
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => changes.SaveItemsAsync(project.Id, [draft], Guid.NewGuid()));
            var failing = new ProjectChanges(new TestContextFactory(() => new RejectAuditContext(options)), new UnusedIdentityFactory(), auth);
            var latest = await db.FeatureFlags.AsNoTracking().SingleAsync(); latest.PercentageRollout = 90;
            await Assert.ThrowsAsync<DbUpdateException>(() => failing.SaveItemsAsync(project.Id, [latest], Guid.NewGuid()));
            Assert.Equal(55, (await db.FeatureFlags.AsNoTracking().SingleAsync()).PercentageRollout);
            Assert.Equal(3, await db.AuditEvents.CountAsync());
            using var identityServices = new ServiceCollection().Configure<IdentityOptions>(o => o.Stores.MaxLengthForKeys = 128).BuildServiceProvider();
            var identityOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseApplicationServiceProvider(identityServices).UseNpgsql(connectionString + ";Search Path=" + schema).Options;
            await using var identity = new ApplicationDbContext(identityOptions); await identity.Database.MigrateAsync();
            identity.Users.Add(new ApplicationUser { Id = "member", Email = "member@example.com", NormalizedEmail = "MEMBER@EXAMPLE.COM", UserName = "Member" });
            await identity.SaveChangesAsync();
            var membership = new ProjectChanges(factory, new IdentityFactory(identityOptions), auth);
            Assert.Equal("member.added", Assert.Single(await membership.AddMemberAsync(project.Id, "member@example.com", ProjectRole.Editor, Guid.NewGuid())).Action);
            var member = await db.ProjectMembers.AsNoTracking().SingleAsync(m => m.UserId == "member");
            var roleEvent = Assert.Single(await membership.ChangeMemberAsync(project.Id, member.Id, member.Revision, ProjectRole.Viewer, Guid.NewGuid()));
            Assert.Contains("Editor", roleEvent.Before); Assert.Contains("Viewer", roleEvent.After);
            member = await db.ProjectMembers.AsNoTracking().SingleAsync(m => m.UserId == "member");
            Assert.Equal("member.removed", Assert.Single(await membership.ChangeMemberAsync(project.Id, member.Id, member.Revision, null, Guid.NewGuid())).Action);
            Assert.Equal("member.restored", Assert.Single(await membership.AddMemberAsync(project.Id, "member@example.com", ProjectRole.Admin, Guid.NewGuid())).Action);
        }
        finally { await using var drop = new NpgsqlCommand($"DROP SCHEMA {schema} CASCADE", connection); await drop.ExecuteNonQueryAsync(); }
    }

    private sealed class TestMutation(IDbContextFactory<FeatureFlagDbContext> factory, AuthenticationStateProvider auth) : ProjectMutation(factory, auth)
    {
        public Task<IReadOnlyList<AuditEvent>> Run(string project, Func<MutationContext, Task> action)
            => ExecuteAsync(project, Guid.NewGuid(), ProjectRole.Editor, action);
    }
    private sealed class RejectAuditContext(DbContextOptions<FeatureFlagDbContext> options) : FeatureFlagDbContext(options)
    {
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => ChangeTracker.Entries<AuditEvent>().Any(e => e.State == EntityState.Added)
                ? throw new DbUpdateException("Simulated audit storage failure") : base.SaveChangesAsync(cancellationToken);
    }
    private sealed class IdentityFactory(DbContextOptions<ApplicationDbContext> options) : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
    }
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        public DbContextOptions<FeatureFlagDbContext> Options { get; private set; } = default!;
        public TestContextFactory Factory { get; private set; } = default!;
        public AuthenticationStateProvider Auth { get; } = Authentication();
        public ProjectChanges Changes { get; private set; } = default!;
        public Project Project { get; } = new() { Name = "Test project", Environments = { new() { Name = "Development" } },
            Members = { new() { UserId = "owner", Email = "owner@example.com", DisplayName = "Owner", Role = ProjectRole.Owner } } };
        public int EnvironmentId => Project.Environments.Single().Id;
        public static AuthenticationStateProvider Authentication() => new BunitAuthenticationStateProvider("owner@example.com", [], [new Claim(ClaimTypes.NameIdentifier, "owner")], "Test");
        public static async Task<Fixture> CreateAsync()
        {
            var f = new Fixture(); await f.connection.OpenAsync();
            f.Options = new DbContextOptionsBuilder<FeatureFlagDbContext>().UseSqlite(f.connection).Options;
            f.Factory = new TestContextFactory(() => new FeatureFlagDbContext(f.Options));
            f.Changes = new ProjectChanges(f.Factory, new UnusedIdentityFactory(), f.Auth);
            await using var db = f.Factory.CreateDbContext(); await db.Database.EnsureCreatedAsync();
            db.Projects.Add(f.Project); await db.SaveSeedChangesAsync();
            db.FeatureFlags.Add(new FeatureFlag { Name = "Alpha", PercentageRollout = 10, ProjectEnvironmentId = f.EnvironmentId });
            await db.SaveSeedChangesAsync(); return f;
        }
        public async Task<FeatureFlag> FlagAsync() { await using var db = Factory.CreateDbContext(); return await db.FeatureFlags.AsNoTracking().SingleAsync(); }
        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }
}
