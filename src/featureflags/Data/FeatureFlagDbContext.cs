using Microsoft.EntityFrameworkCore;
using FeatureFlags.Components.Models;

namespace FeatureFlags.Data;

public class FeatureFlagDbContext(DbContextOptions<FeatureFlagDbContext> options) : DbContext(options)
{
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<FeatureFlag> FeatureFlags => Set<FeatureFlag>();

    public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();

    public DbSet<ProjectEnvironment> ProjectEnvironments => Set<ProjectEnvironment>();

    public DbSet<ClientKey> ClientKeys => Set<ClientKey>();

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

    modelBuilder.Entity<ProjectMember>()
    .HasIndex(m => new { m.ProjectId, m.UserId })
    .IsUnique();

modelBuilder.Entity<ProjectMember>()
    .HasOne(m => m.Project)
    .WithMany(p => p.Members)
    .HasForeignKey(m => m.ProjectId);
}
}