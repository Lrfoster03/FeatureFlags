using System.Security.Claims;
using Bunit;
using FeatureFlags.Components.Pages;
using FeatureFlags.Data;
using FeatureFlags.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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
