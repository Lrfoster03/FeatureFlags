using Microsoft.EntityFrameworkCore;
using FeatureFlags.Components.Models;

namespace FeatureFlags.Data;

public class FeatureFlagDbContext(DbContextOptions<FeatureFlagDbContext> options) : DbContext(options)
{
    public DbSet<FeatureFlag> FeatureFlags => Set<FeatureFlag>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FeatureFlag>()
            .HasIndex(f => f.Name)
            .IsUnique();
    }
}