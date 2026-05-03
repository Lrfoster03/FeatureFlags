using Microsoft.EntityFrameworkCore;
using FeatureFlags.Components.Models;

namespace FeatureFlags.Data;

public class FeatureFlagDbContext(DbContextOptions<FeatureFlagDbContext> options) : DbContext(options)
{
    public DbSet<FeatureFlag> FeatureFlags => Set<FeatureFlag>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<Project>()
        .HasIndex(p => p.Name)
        .IsUnique();

    modelBuilder.Entity<ProjectEnvironment>()
        .HasIndex(e => new { e.ProjectId, e.Name })
        .IsUnique();

    modelBuilder.Entity<FeatureFlag>()
        .HasIndex(f => new { f.ProjectEnvironmentId, f.Name })
        .IsUnique();

    modelBuilder.Entity<ClientKey>()
        .HasIndex(k => k.Key)
        .IsUnique();
}
}