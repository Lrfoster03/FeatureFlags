using FeatureFlags.Data;
using Microsoft.EntityFrameworkCore;

namespace FeatureFlags.Tests;

internal static class AuditTestSupport
{
    // Fixture setup only, never an application/runtime bypass. Mutation tests use the public guard.
    public static Task<int> SaveSeedChangesAsync(this FeatureFlagDbContext db) => db.SaveMutationAsync(
        db.ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(e => e.Entity).ToHashSet(ReferenceEqualityComparer.Instance));
    public static int SaveSeedChanges(this FeatureFlagDbContext db) => db.SaveSeedChangesAsync().GetAwaiter().GetResult();
}

internal sealed class TestContextFactory(Func<FeatureFlagDbContext> create) : IDbContextFactory<FeatureFlagDbContext>
{
    public FeatureFlagDbContext CreateDbContext() => create();
}

internal sealed class UnusedIdentityFactory : IDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext() => throw new InvalidOperationException("This test does not use Identity.");
}
