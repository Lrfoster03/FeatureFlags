using FeatureFlags.Components.Models;
using FeatureFlags.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Nodes;

namespace FeatureFlags.Tests;

public class FeatureFlagDbContextTests
{
    [Fact]
    public void FeatureFlags_DbSet_Is_Available()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        using var context = CreateContext(connection);

        Assert.NotNull(context.FeatureFlags);
    }

    [Fact]
    public void OnModelCreating_Configures_Unique_Index_On_Name()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        using var context = CreateContext(connection);

        var entityType = context.Model.FindEntityType(typeof(FeatureFlag));

        Assert.NotNull(entityType);

        var nameIndex = entityType!.GetIndexes().Single(i =>
            i.Properties.Select(p => p.Name).SequenceEqual([
                nameof(FeatureFlag.ProjectEnvironmentId),
                nameof(FeatureFlag.Name)
            ]));

        Assert.True(nameIndex.IsUnique);
    }

    [Fact]
    public async Task SaveChanges_Enforces_Unique_Name_Index()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        await using var context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync();
        var environmentId = await SeedProjectEnvironmentAsync(context);

        context.FeatureFlags.Add(new FeatureFlag
        {
            Name = "Alpha",
            Description = "First",
            PercentageRollout = 100,
            ProjectEnvironmentId = environmentId
        });
        await context.SaveSeedChangesAsync();

        context.FeatureFlags.Add(new FeatureFlag
        {
            Name = "Alpha",
            Description = "Duplicate",
            PercentageRollout = 0,
            ProjectEnvironmentId = environmentId
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveSeedChangesAsync());
    }

    [Fact]
    public async Task SaveChanges_Persists_Config_Json_Value()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        await using (var context = CreateContext(connection))
        {
            await context.Database.EnsureCreatedAsync();
            var environmentId = await SeedProjectEnvironmentAsync(context);

            context.Configs.Add(new FeatureConfig
            {
                Name = "CheckoutConfig",
                Description = "Checkout settings",
                ProjectEnvironmentId = environmentId,
                Schema = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["enabled"] = new JsonObject { ["type"] = "boolean" }
                    }
                },
                Value = new JsonObject
                {
                    ["enabled"] = true,
                    ["limit"] = 5
                }
            });

            await context.SaveSeedChangesAsync();
        }

        await using (var assertContext = CreateContext(connection))
        {
            var config = await assertContext.Configs.SingleAsync(c => c.Name == "CheckoutConfig");

            Assert.True(config.Value["enabled"]!.GetValue<bool>());
            Assert.Equal(5, config.Value["limit"]!.GetValue<int>());
            Assert.Equal("object", config.Schema["type"]!.GetValue<string>());
            Assert.Equal("boolean", config.Schema["properties"]!["enabled"]!["type"]!.GetValue<string>());
        }
    }

    [Fact]
    public async Task SaveChanges_Rejects_Stale_Flag_Updates()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var first = CreateContext(connection);
        await first.Database.EnsureCreatedAsync();
        var environmentId = await SeedProjectEnvironmentAsync(first);
        var flag = new FeatureFlag { Name = "Checkout", ProjectEnvironmentId = environmentId };
        first.FeatureFlags.Add(flag);
        await first.SaveSeedChangesAsync();
        Assert.Equal(1, flag.Revision);

        await using var stale = CreateContext(connection);
        var staleFlag = await stale.FeatureFlags.SingleAsync();
        flag.PercentageRollout = 25;
        first.SaveSeedChanges();
        Assert.Equal(2, flag.Revision);
        Assert.Equal(0, first.SaveSeedChanges());
        Assert.Equal(2, flag.Revision);
        Assert.Equal(1, (await first.ProjectEnvironments.SingleAsync()).Revision);

        staleFlag.PercentageRollout = 75;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveSeedChangesAsync());
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveSeedChangesAsync());
        Assert.Equal(1, stale.Entry(staleFlag).Property(f => f.Revision).OriginalValue);
        Assert.Equal(2, staleFlag.Revision);

        await using var verify = CreateContext(connection);
        var saved = await verify.FeatureFlags.SingleAsync();
        Assert.Equal(25, saved.PercentageRollout);
        Assert.Equal(2, saved.Revision);
    }

    [Fact]
    public async Task SaveChanges_Rejects_Stale_Config_Json_Updates_And_Deletes()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var first = CreateContext(connection);
        await first.Database.EnsureCreatedAsync();
        var environmentId = await SeedProjectEnvironmentAsync(first);
        var config = new FeatureConfig { Name = "Checkout", ProjectEnvironmentId = environmentId,
            Value = JsonNode.Parse("{\"checkout\":{\"limit\":5}}")!.AsObject() };
        first.Configs.Add(config);
        await first.SaveSeedChangesAsync();
        Assert.Equal(1, config.Revision);

        await using var stale = CreateContext(connection);
        var staleConfig = await stale.Configs.SingleAsync();
        config.Value["checkout"]!["limit"] = 10;
        await first.SaveSeedChangesAsync();
        Assert.Equal(2, config.Revision);
        config.Schema["type"] = "object";
        await first.SaveSeedChangesAsync();
        Assert.Equal(3, config.Revision);

        staleConfig.Value["checkout"]!["limit"] = 20;
        Assert.Throws<DbUpdateConcurrencyException>(() => stale.SaveSeedChanges());
        stale.Configs.Remove(staleConfig);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveSeedChangesAsync());

        await using var verify = CreateContext(connection);
        var saved = await verify.Configs.SingleAsync();
        Assert.Equal(10, saved.Value["checkout"]!["limit"]!.GetValue<int>());
        Assert.Equal("object", saved.Schema["type"]!.GetValue<string>());
        Assert.Equal(3, saved.Revision);
    }

    [Fact]
    public void Factory_Creates_Postgres_DbContext_With_Expected_Connection_String()
    {
        var factory = new FeatureFlagDbContextFactory();

        using var context = factory.CreateDbContext([]);

        var connectionString = context.Database.GetConnectionString();

        Assert.NotNull(connectionString);
        Assert.Contains(FeatureFlagDbContextFactory.DefaultConnectionString, connectionString);
        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", context.Database.ProviderName);
    }

    private static FeatureFlagDbContext CreateContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<FeatureFlagDbContext>()
            .UseSqlite(connection)
            .Options;

        return new FeatureFlagDbContext(options);
    }

    private static async Task<int> SeedProjectEnvironmentAsync(FeatureFlagDbContext context)
    {
        var project = new Project
        {
            Name = "Test Project",
            Environments =
            {
                new ProjectEnvironment { Name = "Development" }
            }
        };

        context.Projects.Add(project);
        await context.SaveSeedChangesAsync();

        return project.Environments.Single().Id;
    }
}
