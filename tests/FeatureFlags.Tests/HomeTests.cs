using Bunit;
using FeatureFlags.Components.Models;
using FeatureFlags.Components.Pages;
using FeatureFlags.Components.Shared;
using FeatureFlags.Data;
using FeatureFlags.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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
        Services.AddScoped(_ => database.CreateContext());

        var cut = Render<Home>();

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
        Services.AddScoped(_ => database.CreateContext());

        var cut = Render<Home>();
        var firstPill = cut.FindComponents<Pill>().Single(p => p.Instance.FeatureFlag.Id == 1);
        var updatedFlag = new FeatureFlag { Id = 1, Name = "  Beta  ", Description = "Updated", PercentageRollout = 50 };

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
        Services.AddScoped(_ => database.CreateContext());

        var cut = Render<Home>();
        var firstPill = cut.FindComponent<Pill>();
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
        Services.AddScoped<FeatureFlagDbContext>(_ => new ThrowingFeatureFlagDbContext(
            database.CreateOptions(),
            throwOnModifiedSave: true));

        var cut = Render<Home>();
        var firstPill = cut.FindComponent<Pill>();
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
        Services.AddScoped(_ => database.CreateContext());

        var cut = Render<Home>();
        var firstPill = cut.FindComponent<Pill>();

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
        Services.AddScoped(_ => database.CreateContext());

        var cut = Render<Home>();
        var firstPill = cut.FindComponent<Pill>();
        var missingFlag = new FeatureFlag { Id = 999, Name = "Alpha" };

        await cut.InvokeAsync(() => firstPill.Instance.OnDelete.InvokeAsync(missingFlag));

        Assert.Contains("Feature flag 'Alpha' was not found.", cut.Markup);
    }

    [Fact]
    public async Task Home_Shows_Error_When_Delete_Throws()
    {
        using var database = new TestDatabase();
        database.Seed(new FeatureFlag { Id = 1, Name = "Alpha", Description = "First", PercentageRollout = 100 });

        Services.AddSingleton<IFeatureFlagConfirmationService>(new StubConfirmationService(true));
        Services.AddScoped<FeatureFlagDbContext>(_ => new ThrowingFeatureFlagDbContext(
            database.CreateOptions(),
            throwOnDeletedSave: true));

        var cut = Render<Home>();
        var firstPill = cut.FindComponent<Pill>();

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
        Services.AddScoped(_ => database.CreateContext());

        var cut = Render<Home>();
        var firstPill = cut.FindComponents<Pill>().Single(p => p.Instance.FeatureFlag.Id == 1);

        await cut.InvokeAsync(() => firstPill.Instance.OnDelete.InvokeAsync(firstPill.Instance.FeatureFlag));

        using var assertContext = database.CreateContext();
        Assert.Single(assertContext.FeatureFlags);
        Assert.Equal("Beta", assertContext.FeatureFlags.Single().Name);
        Assert.DoesNotContain("Alpha", cut.Markup);
    }

    private sealed class StubConfirmationService(bool shouldConfirm) : IFeatureFlagConfirmationService
    {
        public Task<bool> ConfirmDeleteAsync(BlazorBootstrap.ConfirmDialog dialog, string featureFlagName)
            => Task.FromResult(shouldConfirm);
    }

    private sealed class TestDatabase : IDisposable
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");

        public TestDatabase()
        {
            connection.Open();

            using var context = CreateContext();
            context.Database.EnsureCreated();
        }

        public FeatureFlagDbContext CreateContext() => new(CreateOptions());

        public DbContextOptions<FeatureFlagDbContext> CreateOptions() =>
            new DbContextOptionsBuilder<FeatureFlagDbContext>()
                .UseSqlite(connection)
                .Options;

        public void Seed(params FeatureFlag[] flags)
        {
            using var context = CreateContext();
            context.FeatureFlags.AddRange(flags);
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
