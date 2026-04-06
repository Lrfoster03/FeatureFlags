using FeatureFlags.Components.Models;
using FeatureFlags.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

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
            i.Properties.Select(p => p.Name).SequenceEqual([nameof(FeatureFlag.Name)]));

        Assert.True(nameIndex.IsUnique);
    }

    [Fact]
    public async Task SaveChanges_Enforces_Unique_Name_Index()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        await using var context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync();

        context.FeatureFlags.Add(new FeatureFlag
        {
            Name = "Alpha",
            Description = "First",
            IsEnabled = true
        });
        await context.SaveChangesAsync();

        context.FeatureFlags.Add(new FeatureFlag
        {
            Name = "Alpha",
            Description = "Duplicate",
            IsEnabled = false
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public void Factory_Creates_Sqlite_DbContext_With_Expected_Connection_String()
    {
        var factory = new FeatureFlagDbContextFactory();

        using var context = factory.CreateDbContext([]);

        var connectionString = context.Database.GetConnectionString();

        Assert.NotNull(connectionString);
        Assert.Contains("Data Source=featureflags.db", connectionString);
        Assert.Equal("Microsoft.EntityFrameworkCore.Sqlite", context.Database.ProviderName);
    }

    private static FeatureFlagDbContext CreateContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<FeatureFlagDbContext>()
            .UseSqlite(connection)
            .Options;

        return new FeatureFlagDbContext(options);
    }
}
