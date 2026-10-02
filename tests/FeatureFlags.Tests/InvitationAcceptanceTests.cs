using System.Security.Claims;
using Bunit;
using FeatureFlags.Components.Pages;
using FeatureFlags.Data;
using FeatureFlags.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FeatureFlags.Tests;

public class InvitationAcceptanceTests : BunitContext
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Email_link_prompts_for_signup_or_login_and_preserves_the_invitation(bool registered)
    {
        await using var f = await InvitationFixture.CreateAsync();
        if (registered) await f.AddUser("recipient", "recipient@example.com");
        var issued = await f.As("owner").CreateAsync(f.ProjectId, "recipient@example.com", FeatureFlags.Components.Models.ProjectRole.Viewer, Guid.NewGuid());
        Configure(f, new AnonymousAuthentication());
        Services.GetRequiredService<NavigationManager>().NavigateTo("/invitations/accept?token=" + issued.Token);
        var cut = Render<AcceptInvitation>();
        cut.WaitForAssertion(() => Assert.Contains("Join Invitation project", cut.Markup));
        var primary = cut.Find("a.btn-primary");
        Assert.Equal(registered ? "Log in" : "Create account", primary.TextContent);
        Assert.Contains(registered ? "/Login?" : "/Register?", primary.GetAttribute("href"));
        Assert.Contains("returnUrl=%2Finvitations%2Faccept", primary.GetAttribute("href"));
        Assert.Contains("invitationToken=" + issued.Token, primary.GetAttribute("href"));
        await using var db = f.Factory.CreateDbContext();
        Assert.Single(await db.ProjectMembers.ToListAsync());
    }

    [Fact]
    public async Task Signed_in_recipient_must_click_join_before_membership_is_created()
    {
        await using var f = await InvitationFixture.CreateAsync();
        await f.AddUser("recipient", "recipient@example.com");
        var issued = await f.As("owner").CreateAsync(f.ProjectId, "recipient@example.com", FeatureFlags.Components.Models.ProjectRole.Editor, Guid.NewGuid());
        Configure(f, InvitationFixture.Auth("recipient"));
        Services.GetRequiredService<NavigationManager>().NavigateTo("/invitations/accept?token=" + issued.Token);
        var cut = Render<AcceptInvitation>();
        cut.WaitForAssertion(() => Assert.Equal("Join project", cut.Find("button").TextContent));
        await using var db = f.Factory.CreateDbContext();
        Assert.Single(await db.ProjectMembers.ToListAsync());
        cut.Find("button").Click();
        cut.WaitForAssertion(() => Assert.EndsWith($"/projects/{f.ProjectId}/home", Services.GetRequiredService<NavigationManager>().Uri));
        Assert.Equal(2, await db.ProjectMembers.CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unexpected_database_errors_are_logged_safely_and_not_reported_as_conflicts(bool conflict)
    {
        await using var f = await InvitationFixture.CreateAsync();
        await f.AddUser("recipient", "recipient@example.com");
        var issued = await f.As("owner").CreateAsync(f.ProjectId, "recipient@example.com", FeatureFlags.Components.Models.ProjectRole.Viewer, Guid.NewGuid());
        Configure(f, InvitationFixture.Auth("recipient"));
        Services.AddSingleton<IDbContextFactory<FeatureFlagDbContext>>(new TestContextFactory(() => new FailingAcceptanceContext(f.Options, conflict)));
        var logger = new CapturedLogger<AcceptInvitation>();
        Services.AddSingleton<ILogger<AcceptInvitation>>(logger);
        Services.GetRequiredService<NavigationManager>().NavigateTo("/invitations/accept?token=" + issued.Token);
        var cut = Render<AcceptInvitation>();
        cut.WaitForAssertion(() => Assert.Equal("Join project", cut.Find("button").TextContent));
        cut.Find("button").Click();
        cut.WaitForAssertion(() => Assert.Contains(conflict ? "The invitation changed." : "Unable to join the project.", cut.Markup));
        if (conflict) Assert.Empty(logger.Entries);
        else
        {
            var entry = Assert.Single(logger.Entries);
            Assert.Null(entry.Exception);
            Assert.Contains(nameof(DbUpdateException), entry.Message);
            Assert.DoesNotContain("secret-payload", entry.Message);
            Assert.DoesNotContain(issued.Token!, entry.Message);
        }
        Assert.DoesNotContain("secret-payload", cut.Markup);
    }

    private sealed class FailingAcceptanceContext(DbContextOptions<FeatureFlagDbContext> options, bool conflict) : FeatureFlagDbContext(options)
    {
        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
            => throw (conflict ? new DbUpdateConcurrencyException("secret-payload") : new DbUpdateException("secret-payload"));
    }

    private void Configure(InvitationFixture f, AuthenticationStateProvider auth)
    {
        Services.AddSingleton<IDbContextFactory<FeatureFlagDbContext>>(f.Factory);
        Services.AddSingleton(f.IdentityFactory);
        Services.AddSingleton<TimeProvider>(f.Clock);
        Services.AddSingleton(auth);
        Services.AddScoped<ProjectInvitations>();
        Services.AddAuthorization();
        Services.AddCascadingAuthenticationState();
    }
    private sealed class AnonymousAuthentication : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));
    }
}

internal sealed class CapturedLogger<T> : ILogger<T>
{
    public List<(string Message, Exception? Exception)> Entries { get; } = [];
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => Entries.Add((formatter(state, exception), exception));
}
