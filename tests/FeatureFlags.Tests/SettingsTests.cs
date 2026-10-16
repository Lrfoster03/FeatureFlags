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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
        var invitations = new ProjectInvitations(factory, new UnusedIdentityFactory(), auth, TimeProvider.System);
        Services.AddSingleton(invitations);
        Services.AddSingleton(new InvitationDelivery(invitations, new InvitationEmail(
            Microsoft.Extensions.Options.Options.Create(InvitationEmailTests.Settings(Path.GetTempPath())), new InvitationEmailTests.EmailEnvironment())));
        Services.AddSingleton(new ProjectChanges(factory, auth));
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

    [Fact]
    public async Task Delivery_failure_followed_by_refresh_failure_preserves_error_and_keeps_component_usable()
    {
        await using var f = await InvitationFixture.CreateAsync();
        var failRefresh = false;
        Services.AddSingleton<IDbContextFactory<FeatureFlagDbContext>>(new TestContextFactory(() =>
        {
            if (!failRefresh) return f.Factory.CreateDbContext();
            failRefresh = false;
            throw new InvalidOperationException("secret-refresh-payload");
        }));
        var invitations = f.As("owner");
        Services.AddSingleton(invitations);
        var smtp = new InvitationEmailTests.TestSmtpClient("auth", () => failRefresh = true);
        var email = new InvitationEmail(Options.Create(InvitationEmailTests.SmtpSettings()), new InvitationEmailTests.EmailEnvironment(), () => smtp);
        Services.AddSingleton(new InvitationDelivery(invitations, email));
        Services.AddSingleton(new ProjectChanges(f.Factory, InvitationFixture.Auth("owner"), f.Clock));
        Services.AddSingleton<IProjectPermissionService>(new ProjectPermissionService(f.Factory));
        Services.AddSingleton<AuthenticationStateProvider>(InvitationFixture.Auth("owner"));
        var logger = new CapturedLogger<Settings>();
        Services.AddSingleton<ILogger<Settings>>(logger);
        Services.AddAuthorization();
        Services.AddCascadingAuthenticationState();
        var cut = Render<Settings>(p => p.Add(c => c.ProjectId, f.ProjectId));
        cut.Find("#invite-email").Change("recipient@example.com");
        cut.Find("button[aria-label='Send invitation']").Click();
        cut.WaitForAssertion(() => Assert.Contains("The invitation was saved, but its email could not be sent.", cut.Markup));
        Assert.False(cut.Find("fieldset").HasAttribute("disabled"));
        Assert.Equal(2, logger.Entries.Count);
        Assert.Contains(logger.Entries, e => e.Message.Contains("delivery") && e.Message.Contains("AuthenticationException"));
        Assert.Contains(logger.Entries, e => e.Message.Contains("recovery refresh"));
        Assert.All(logger.Entries, e =>
        {
            Assert.Null(e.Exception);
            Assert.DoesNotContain("secret-", e.Message);
            Assert.DoesNotContain("test-only-password", e.Message);
        });
        Assert.DoesNotContain("secret-", cut.Markup);
        await using var db = f.Factory.CreateDbContext();
        Assert.Single(await db.ProjectInvitations.ToListAsync());
        // A subsequent click is handled normally and refreshes the saved pending invitation.
        cut.Find("button[aria-label='Send invitation']").Click();
        cut.WaitForAssertion(() => Assert.Contains("Use Resend", cut.Markup));
        Assert.Contains("recipient@example.com", cut.Find("table[aria-label='Pending invitations']").TextContent);
        Assert.Equal(2, logger.Entries.Count);
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
