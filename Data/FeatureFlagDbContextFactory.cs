using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FeatureFlags.Data;

public class FeatureFlagDbContextFactory : IDesignTimeDbContextFactory<FeatureFlagDbContext>
{
    public FeatureFlagDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<FeatureFlagDbContext>();

        optionsBuilder.UseSqlite("Data Source=featureflags.db");

        return new FeatureFlagDbContext(optionsBuilder.Options);
    }
}