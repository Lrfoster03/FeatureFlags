using FeatureFlags.Components.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FeatureFlags.Data;

public class FeatureFlagDbContext(DbContextOptions<FeatureFlagDbContext> options) : DbContext(options)
{
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<FeatureFlag> FeatureFlags => Set<FeatureFlag>();
    public DbSet<FeatureConfig> Configs => Set<FeatureConfig>();
    public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();
    public DbSet<ProjectEnvironment> ProjectEnvironments => Set<ProjectEnvironment>();
    public DbSet<ClientKey> ClientKeys => Set<ClientKey>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var audit = modelBuilder.Entity<AuditEvent>();
        audit.Property(e => e.Before).HasColumnType("jsonb");
        audit.Property(e => e.After).HasColumnType("jsonb");
        audit.HasIndex(e => new { e.ProjectId, e.OccurredAtUtc, e.Id }).IsDescending(false, true, true);
        audit.HasIndex(e => new { e.ProjectId, e.EntityType, e.EntityId, e.OccurredAtUtc, e.Id })
            .IsDescending(false, false, false, true, true);
        audit.HasIndex(e => new { e.OperationId, e.EntityType, e.EntityId }).IsUnique();

        foreach (var type in new[] { typeof(Project), typeof(ProjectEnvironment), typeof(ProjectMember),
                     typeof(FeatureFlag), typeof(FeatureConfig), typeof(ClientKey) })
            modelBuilder.Entity(type).Property<int>("Revision").IsConcurrencyToken();

        modelBuilder.Entity<Project>()
            .HasIndex(p => p.Name)
            .IsUnique();

        modelBuilder.Entity<ProjectEnvironment>()
            .HasIndex(e => new { e.ProjectId, e.Name })
            .IsUnique();

        modelBuilder.Entity<FeatureFlag>()
            .HasIndex(f => new { f.ProjectEnvironmentId, f.Name })
            .IsUnique();

        modelBuilder.Entity<FeatureConfig>()
            .HasIndex(f => new { f.ProjectEnvironmentId, f.Name })
            .IsUnique();

        modelBuilder.Entity<FeatureConfig>()
            .Property(c => c.Value)
            .HasConversion(
                value => value.ToJsonString(JsonStorageOptions),
                value => ParseJsonObject(value))
            .Metadata.SetValueComparer(JsonObjectComparer);

        modelBuilder.Entity<FeatureConfig>()
            .Property(c => c.Schema)
            .HasConversion(
                value => value.ToJsonString(JsonStorageOptions),
                value => ParseJsonObject(value))
            .Metadata.SetValueComparer(JsonObjectComparer);

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

    private static readonly JsonSerializerOptions JsonStorageOptions = new(JsonSerializerDefaults.Web);

    private static readonly ValueComparer<JsonObject> JsonObjectComparer = new(
        (left, right) => JsonNode.DeepEquals(left, right),
        value => value.ToJsonString(JsonStorageOptions).GetHashCode(StringComparison.Ordinal),
        value => CloneJsonObject(value));

    private static JsonObject ParseJsonObject(string value)
    {
        return JsonNode.Parse(value)?.AsObject() ?? new JsonObject();
    }

    private static JsonObject CloneJsonObject(JsonObject value)
    {
        return ParseJsonObject(value.ToJsonString(JsonStorageOptions));
    }
}
