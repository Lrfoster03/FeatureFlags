using Bunit;
using Bunit.TestDoubles;
using FeatureFlags.Components.Models;
using FeatureFlags.Components.Pages;
using FeatureFlags.Components.Shared;
using FeatureFlags.Data;
using FeatureFlags.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;
using System.Text.Json.Nodes;

namespace FeatureFlags.Tests;

public class HomeTests : BunitContext
{
    [Fact]
    public void Home_Loads_Flags_And_Adds_New_Flag()
    {
        using var database = new TestDatabase();
        database.Seed(
            new FeatureFlag { Id = 1, Name = "Alpha", Description = "First", PercentageRollout = 100 },
            new FeatureFlag { Id = 2, Name = "Beta", Description = "Second", PercentageRollout = 0 });

        Services.AddSingleton<IFeatureFlagConfirmationService>(new StubConfirmationService(false));
        Services.AddSingleton<IProjectPermissionService>(new StubProjectPermissionService());
        Services.AddScoped(_ => database.CreateContext());
        AddAuthenticatedUser();

        var cut = RenderHome(database);

        Assert.Contains("Alpha", cut.Markup);
        Assert.Contains("Beta", cut.Markup);

        cut.Find("button.btn.btn-primary").Click();

        using var assertContext = database.CreateContext();
        var created = assertContext.FeatureFlags.Single(f => f.Name == "Item 3");

        Assert.Equal(3, assertContext.FeatureFlags.Count());
        Assert.Equal(string.Empty, created.Description);
        Assert.Equal(0, created.PercentageRollout);
        Assert.Contains("Item 3", cut.Markup);
    }

    [Fact]
    public async Task Home_Shows_Error_When_Saving_Duplicate_Name()
    {
        using var database = new TestDatabase();
        database.Seed(
            new FeatureFlag { Id = 1, Name = "Alpha", Description = "First", PercentageRollout = 100 },
            new FeatureFlag { Id = 2, Name = "Beta", Description = "Second", PercentageRollout = 0 });

        Services.AddSingleton<IFeatureFlagConfirmationService>(new StubConfirmationService(false));
        Services.AddSingleton<IProjectPermissionService>(new StubProjectPermissionService());
        Services.AddScoped(_ => database.CreateContext());
        AddAuthenticatedUser();

        var cut = RenderHome(database);
        var firstPill = cut.FindComponents<FlagPill>().Single(p => p.Instance.FeatureFlag.Id == 1);
        var updatedFlag = new FeatureFlag { Id = 1, Name = "  Beta  ", Description = "Updated", PercentageRollout = 50, ProjectEnvironmentId = database.EnvironmentId };

        await cut.InvokeAsync(() => firstPill.Instance.OnChanged.InvokeAsync(updatedFlag));

        Assert.Contains("A feature flag named 'Beta' already exists.", cut.Markup);

        using var assertContext = database.CreateContext();
        var persisted = assertContext.FeatureFlags.Single(f => f.Id == 1);
        Assert.Equal("Alpha", persisted.Name);
        Assert.Equal("First", persisted.Description);
        Assert.Equal(100, persisted.PercentageRollout);
    }

    [Fact]
    public async Task Home_Saves_Updated_Flag_When_Name_Is_Unique()
    {
        using var database = new TestDatabase();
        database.Seed(new FeatureFlag { Id = 1, Name = "Alpha", Description = "First", PercentageRollout = 100 });

        Services.AddSingleton<IFeatureFlagConfirmationService>(new StubConfirmationService(false));
        Services.AddSingleton<IProjectPermissionService>(new StubProjectPermissionService());
        Services.AddScoped(_ => database.CreateContext());
        AddAuthenticatedUser();

        var cut = RenderHome(database);
        var firstPill = cut.FindComponent<FlagPill>();
        var updatedFlag = firstPill.Instance.FeatureFlag;
        updatedFlag.Name = "  Gamma  ";
        updatedFlag.Description = "Updated";
        updatedFlag.PercentageRollout = 42;

        await cut.InvokeAsync(() => firstPill.Instance.OnChanged.InvokeAsync(updatedFlag));

        using var assertContext = database.CreateContext();
        var persisted = assertContext.FeatureFlags.Single(f => f.Id == 1);

        Assert.Equal("Gamma", persisted.Name);
        Assert.Equal("Updated", persisted.Description);
        Assert.Equal(42, persisted.PercentageRollout);
        Assert.DoesNotContain("already exists", cut.Markup);
    }

    [Fact]
    public async Task Home_Shows_Error_When_Save_Throws_DbUpdateException()
    {
        using var database = new TestDatabase();
        database.Seed(new FeatureFlag { Id = 1, Name = "Alpha", Description = "First", PercentageRollout = 100 });

        Services.AddSingleton<IFeatureFlagConfirmationService>(new StubConfirmationService(false));
        Services.AddSingleton<IProjectPermissionService>(new StubProjectPermissionService());
        Services.AddScoped<FeatureFlagDbContext>(_ => new ThrowingFeatureFlagDbContext(
            database.CreateOptions(),
            throwOnModifiedSave: true));
        AddAuthenticatedUser();

        var cut = RenderHome(database);
        var firstPill = cut.FindComponent<FlagPill>();
        var updatedFlag = firstPill.Instance.FeatureFlag;
        updatedFlag.Name = "Gamma";
        updatedFlag.Description = "Updated";
        updatedFlag.PercentageRollout = 25;

        await cut.InvokeAsync(() => firstPill.Instance.OnChanged.InvokeAsync(updatedFlag));

        Assert.Contains("A feature flag named 'Gamma' already exists.", cut.Markup);
    }

    [Fact]
    public async Task Home_Delete_Returns_Without_Removing_When_Not_Confirmed()
    {
        using var database = new TestDatabase();
        database.Seed(new FeatureFlag { Id = 1, Name = "Alpha", Description = "First", PercentageRollout = 100 });

        Services.AddSingleton<IFeatureFlagConfirmationService>(new StubConfirmationService(false));
        Services.AddSingleton<IProjectPermissionService>(new StubProjectPermissionService());
        Services.AddScoped(_ => database.CreateContext());
        AddAuthenticatedUser();

        var cut = RenderHome(database);
        var firstPill = cut.FindComponent<FlagPill>();

        await cut.InvokeAsync(() => firstPill.Instance.OnDelete.InvokeAsync(firstPill.Instance.FeatureFlag));

        using var assertContext = database.CreateContext();
        Assert.Equal(1, assertContext.FeatureFlags.Count());
        Assert.DoesNotContain("was not found", cut.Markup);
        Assert.DoesNotContain("Failed to delete", cut.Markup);
    }

    [Fact]
    public async Task Home_Shows_Error_When_Deleting_Missing_Flag()
    {
        using var database = new TestDatabase();
        database.Seed(new FeatureFlag { Id = 1, Name = "Alpha", Description = "First", PercentageRollout = 100 });

        Services.AddSingleton<IFeatureFlagConfirmationService>(new StubConfirmationService(true));
        Services.AddSingleton<IProjectPermissionService>(new StubProjectPermissionService());
        Services.AddScoped(_ => database.CreateContext());
        AddAuthenticatedUser();

        var cut = RenderHome(database);
        var firstPill = cut.FindComponent<FlagPill>();
        var missingFlag = new FeatureFlag { Id = 999, Name = "Alpha", ProjectEnvironmentId = database.EnvironmentId };

        await cut.InvokeAsync(() => firstPill.Instance.OnDelete.InvokeAsync(missingFlag));

        Assert.Contains("Feature flag 'Alpha' was not found.", cut.Markup);
    }

    [Fact]
    public async Task Home_Shows_Error_When_Delete_Throws()
    {
        using var database = new TestDatabase();
        database.Seed(new FeatureFlag { Id = 1, Name = "Alpha", Description = "First", PercentageRollout = 100 });

        Services.AddSingleton<IFeatureFlagConfirmationService>(new StubConfirmationService(true));
        Services.AddSingleton<IProjectPermissionService>(new StubProjectPermissionService());
        Services.AddScoped<FeatureFlagDbContext>(_ => new ThrowingFeatureFlagDbContext(
            database.CreateOptions(),
            throwOnDeletedSave: true));
        AddAuthenticatedUser();

        var cut = RenderHome(database);
        var firstPill = cut.FindComponent<FlagPill>();

        await cut.InvokeAsync(() => firstPill.Instance.OnDelete.InvokeAsync(firstPill.Instance.FeatureFlag));

        Assert.Contains("Failed to delete feature flag 'Alpha'.", cut.Markup);
    }

    [Fact]
    public async Task Home_Deletes_Flag_When_Confirmed()
    {
        using var database = new TestDatabase();
        database.Seed(
            new FeatureFlag { Id = 1, Name = "Alpha", Description = "First", PercentageRollout = 100 },
            new FeatureFlag { Id = 2, Name = "Beta", Description = "Second", PercentageRollout = 0 });

        Services.AddSingleton<IFeatureFlagConfirmationService>(new StubConfirmationService(true));
        Services.AddSingleton<IProjectPermissionService>(new StubProjectPermissionService());
        Services.AddScoped(_ => database.CreateContext());
        AddAuthenticatedUser();

        var cut = RenderHome(database);
        var firstPill = cut.FindComponents<FlagPill>().Single(p => p.Instance.FeatureFlag.Id == 1);

        await cut.InvokeAsync(() => firstPill.Instance.OnDelete.InvokeAsync(firstPill.Instance.FeatureFlag));

        using var assertContext = database.CreateContext();
        Assert.Single(assertContext.FeatureFlags);
        Assert.Equal("Beta", assertContext.FeatureFlags.Single().Name);
        Assert.DoesNotContain("Alpha", cut.Markup);
    }

    [Fact]
    public void Home_Renders_Flags_Section_Before_Configs_Section()
    {
        using var database = new TestDatabase();
        database.Seed(
            new FeatureFlag { Id = 1, Name = "Alpha", Description = "First", PercentageRollout = 100 },
            new FeatureFlag { Id = 2, Name = "Beta", Description = "Second", PercentageRollout = 0 });
        database.SeedConfigs(
            new FeatureConfig
            {
                Id = 1,
                Name = "CheckoutConfig",
                Description = "Checkout settings",
                Value = new JsonObject { ["enabled"] = true }
            });

        Services.AddSingleton<IFeatureFlagConfirmationService>(new StubConfirmationService(false));
        Services.AddSingleton<IProjectPermissionService>(new StubProjectPermissionService());
        Services.AddScoped(_ => database.CreateContext());
        AddAuthenticatedUser();

        var cut = RenderHome(database);
        var markup = cut.Markup;

        Assert.Contains("Feature Flags", markup);
        Assert.Contains("Configs", markup);
        Assert.True(markup.IndexOf("Feature Flags", StringComparison.Ordinal) < markup.IndexOf("Configs", StringComparison.Ordinal));
        Assert.True(markup.IndexOf("Alpha", StringComparison.Ordinal) < markup.IndexOf("CheckoutConfig", StringComparison.Ordinal));
    }

    private sealed class StubConfirmationService(bool shouldConfirm) : IFeatureFlagConfirmationService
    {
        public Task<bool> ConfirmDeleteAsync(BlazorBootstrap.ConfirmDialog dialog, string featureFlagName)
            => Task.FromResult(shouldConfirm);
    }

    private IRenderedComponent<Home> RenderHome(TestDatabase database)
        => Render<Home>(parameters => parameters.Add(p => p.ProjectId, database.ProjectId));

    private void AddAuthenticatedUser()
    {
        Services.AddAuthorization();
        Services.AddCascadingAuthenticationState();
        Services.AddSingleton<AuthenticationStateProvider>(
            new BunitAuthenticationStateProvider(
                "owner@example.com",
                [],
                [new Claim(ClaimTypes.NameIdentifier, TestDatabase.UserId)],
                "Test"));
    }

    private sealed class StubProjectPermissionService : IProjectPermissionService
    {
        public Task<bool> CanViewProjectAsync(string projectId, string? userId)
            => Task.FromResult(true);

        public Task<bool> CanEditFlagsAsync(string projectId, string? userId)
            => Task.FromResult(true);

        public Task<bool> CanManageMembersAsync(string projectId, string? userId)
            => Task.FromResult(true);

        public Task<bool> CanManageKeysAsync(string projectId, string? userId)
            => Task.FromResult(true);
    }

    private sealed class TestDatabase : IDisposable
    {
        public const string UserId = "owner-user";

        private readonly SqliteConnection connection = new("Data Source=:memory:");

        public string ProjectId { get; }
        public int EnvironmentId { get; }

        public TestDatabase()
        {
            connection.Open();

            using var context = CreateContext();
            context.Database.EnsureCreated();

            var project = new Project
            {
                Name = "Test Project",
                Environments =
                {
                    new ProjectEnvironment { Name = "Development" }
                },
                Members =
                {
                    new ProjectMember
                    {
                        UserId = UserId,
                        Email = "owner@example.com",
                        DisplayName = "Owner",
                        Role = ProjectRole.Owner
                    }
                }
            };

            context.Projects.Add(project);
            context.SaveChanges();

            ProjectId = project.Id;
            EnvironmentId = project.Environments.Single().Id;
        }

        public FeatureFlagDbContext CreateContext() => new(CreateOptions());

        public DbContextOptions<FeatureFlagDbContext> CreateOptions() =>
            new DbContextOptionsBuilder<FeatureFlagDbContext>()
                .UseSqlite(connection)
                .Options;

        public void Seed(params FeatureFlag[] flags)
        {
            using var context = CreateContext();
            foreach (var flag in flags)
            {
                if (flag.ProjectEnvironmentId == 0)
                    flag.ProjectEnvironmentId = EnvironmentId;
            }

            context.FeatureFlags.AddRange(flags);
            context.SaveChanges();
        }

        public void SeedConfigs(params FeatureConfig[] configs)
        {
            using var context = CreateContext();
            foreach (var config in configs)
            {
                if (config.ProjectEnvironmentId == 0)
                    config.ProjectEnvironmentId = EnvironmentId;
            }

            context.Configs.AddRange(configs);
            context.SaveChanges();
        }

        public void Dispose()
        {
            connection.Dispose();
        }
    }

    private sealed class ThrowingFeatureFlagDbContext(
        DbContextOptions<FeatureFlagDbContext> options,
        bool throwOnModifiedSave = false,
        bool throwOnDeletedSave = false) : FeatureFlagDbContext(options)
    {
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (throwOnModifiedSave && ChangeTracker.Entries<FeatureFlag>().Any(e => e.State == EntityState.Modified))
                throw new DbUpdateException("Simulated duplicate update.");

            if (throwOnDeletedSave && ChangeTracker.Entries<FeatureFlag>().Any(e => e.State == EntityState.Deleted))
                throw new InvalidOperationException("Simulated delete failure.");

            return base.SaveChangesAsync(cancellationToken);
        }
    }
}
