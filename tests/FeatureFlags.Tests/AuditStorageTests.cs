using FeatureFlags.Components.Models;
using FeatureFlags.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FeatureFlags.Tests;

public class AuditStorageTests
{
    [Fact]
    public async Task History_preserves_snapshots_without_resource_or_actor_foreign_keys()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<FeatureFlagDbContext>().UseSqlite(connection).Options;
        await using var db = new FeatureFlagDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var audit = new AuditEvent
        {
            ProjectId = "deleted-project", ActorUserId = "deleted-user", ActorDisplayName = "Original name",
            EntityType = "config", EntityId = "123", EntityName = "Original config", Action = "config.deleted",
            Before = "{\"value\":{\"enabled\":true}}", OperationId = Guid.NewGuid(), OccurredAtUtc = DateTime.UtcNow
        };
        db.AuditEvents.Add(audit);
        await db.SaveSeedChangesAsync();
        db.ChangeTracker.Clear();
        var saved = await db.AuditEvents.SingleAsync();
        Assert.Equal(audit.Before, saved.Before);
        Assert.Null(saved.After);
        Assert.Equal("Original name", saved.ActorDisplayName);
        Assert.Empty(db.Model.FindEntityType(typeof(AuditEvent))!.GetForeignKeys());

        db.AuditEvents.Add(new AuditEvent { ProjectId = audit.ProjectId, OperationId = audit.OperationId,
            EntityType = audit.EntityType, EntityId = audit.EntityId });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveSeedChangesAsync());
    }
}
