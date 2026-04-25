using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FeatureFlags.Data;

public class FeatureFlagDbContextFactory : IDesignTimeDbContextFactory<FeatureFlagDbContext>
{
    public const string ConnectionStringName = "FeatureFlags";
    public const string DefaultConnectionString = "Data Source=featureflags.db";

    public FeatureFlagDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<FeatureFlagDbContext>();

        var connectionString = Environment.GetEnvironmentVariable($"ConnectionStrings__{ConnectionStringName}")
            ?? DefaultConnectionString;

        optionsBuilder.UseSqlite(connectionString);

        return new FeatureFlagDbContext(optionsBuilder.Options);
    }
}
