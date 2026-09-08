using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using FeatureFlags.Components.Models;
using FeatureFlags.Components.Pages;
using FeatureFlags.Data;
using FeatureFlags.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FeatureFlags.Tests;

public class SettingsTests : BunitContext
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Generate_key_retries_committed_operation_after_ui_failure(bool failCompletion)
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<FeatureFlagDbContext>().UseSqlite(connection).Options;
        await using var db = new FeatureFlagDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var project = new Project { Name = "Test", Environments = { new() { Name = "Development" } },
            Members = { new() { UserId = "owner", Role = ProjectRole.Owner } } };
        db.Projects.Add(project);
        await db.SaveSeedChangesAsync();

        var auth = new BunitAuthenticationStateProvider("owner@example.com", [],
            [new Claim(ClaimTypes.NameIdentifier, "owner")], "Test");
        var factory = new TestContextFactory(() => new FeatureFlagDbContext(options));
        var failNextRefresh = false;
        Services.AddSingleton<IDbContextFactory<FeatureFlagDbContext>>(new TestContextFactory(() =>
        {
            if (!failNextRefresh) return new FeatureFlagDbContext(options);
            failNextRefresh = false;
            if (!failCompletion) throw new InvalidOperationException("Simulated refresh failure");
            // A successful refresh with a missing key makes the completion callback fail.
            return new HiddenKeysContext(options);
        }));
        Services.AddSingleton(new ProjectChanges(factory, new UnusedIdentityFactory(), auth));
        Services.AddSingleton<IProjectPermissionService>(new ProjectPermissionService(factory));
        Services.AddSingleton<AuthenticationStateProvider>(auth);
        Services.AddAuthorization();
        Services.AddCascadingAuthenticationState();
        var cut = Render<Settings>(p => p.Add(c => c.ProjectId, project.Id));

        failNextRefresh = true;
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Generate Key").Click();
        cut.WaitForAssertion(() => Assert.Contains("Failed to save changes. Please retry.", cut.Markup));
        var key = await db.ClientKeys.SingleAsync();
        var audit = await db.AuditEvents.SingleAsync();

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Generate Key").Click();
        cut.WaitForAssertion(() => Assert.Contains("API key generated.", cut.Markup));
        Assert.Equal(key.Id, (await db.ClientKeys.SingleAsync()).Id);
        Assert.Equal(audit.Id, (await db.AuditEvents.SingleAsync()).Id);
        Assert.Contains(key.Key, cut.Markup);

        // Once refresh and completion succeed, another click is a new operation.
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Generate Key").Click();
        Assert.Equal(2, await db.ClientKeys.CountAsync());
        Assert.Equal(2, await db.AuditEvents.Select(e => e.OperationId).Distinct().CountAsync());
    }

    private sealed class HiddenKeysContext(DbContextOptions<FeatureFlagDbContext> options) : FeatureFlagDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<ClientKey>().HasQueryFilter(k => false);
        }
    }
}
