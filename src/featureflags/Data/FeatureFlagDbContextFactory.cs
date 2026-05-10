using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FeatureFlags.Data;

public class FeatureFlagDbContextFactory : IDesignTimeDbContextFactory<FeatureFlagDbContext>
{
    public const string ConnectionStringName = "FeatureFlags";
    public const string DefaultConnectionString = "Host=localhost;Port=5432;Database=featureflags;Username=featureflags;Password=featureflags";

    public FeatureFlagDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<FeatureFlagDbContext>();

        var connectionString = Environment.GetEnvironmentVariable($"ConnectionStrings__{ConnectionStringName}")
            ?? DefaultConnectionString;

        optionsBuilder.UseNpgsql(connectionString);

        return new FeatureFlagDbContext(optionsBuilder.Options);
    }
}
