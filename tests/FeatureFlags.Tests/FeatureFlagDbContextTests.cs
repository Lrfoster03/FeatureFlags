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
        await context.SaveChangesAsync();

        context.FeatureFlags.Add(new FeatureFlag
        {
            Name = "Alpha",
            Description = "Duplicate",
            PercentageRollout = 0,
            ProjectEnvironmentId = environmentId
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
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

            await context.SaveChangesAsync();
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
        await context.SaveChangesAsync();

        return project.Environments.Single().Id;
    }
}
