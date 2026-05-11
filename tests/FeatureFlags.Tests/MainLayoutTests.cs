using Bunit;
using Bunit.TestDoubles;
using FeatureFlags.Components.Models;
using FeatureFlags.Components.Layout;
using FeatureFlags.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;

namespace FeatureFlags.Tests;

public class MainLayoutTests : BunitContext
{
    [Fact]
    public void MainLayout_Renders_Body_And_Error_UI()
    {
        using var database = new TestDatabase();
        AddLayoutServices(database);

        var cut = Render<LayoutView>(parameters => parameters
            .Add(p => p.Layout, typeof(MainLayout))
            .AddChildContent("<p id=\"layout-body\">Hello layout</p>"));

        Assert.Contains("Hello layout", cut.Markup);
        Assert.Equal("blazor-error-ui", cut.Find("#blazor-error-ui").Id);
        Assert.Contains("An unhandled error has occurred.", cut.Markup);
        Assert.Equal(".", cut.Find("a.reload").GetAttribute("href"));
        Assert.Contains("Reload", cut.Markup);
        Assert.Contains("🗙", cut.Markup);
    }

    [Fact]
    public void MainLayout_Renders_Projects_Link_And_Project_Switcher()
    {
        using var database = new TestDatabase();
        database.SeedProject("Alpha Project");
        database.SeedProject("Beta Project");
        AddLayoutServices(database, authenticated: true);

        var cut = Render<LayoutView>(parameters => parameters
            .Add(p => p.Layout, typeof(MainLayout))
            .AddChildContent("<p id=\"layout-body\">Hello layout</p>"));

        Assert.Equal("/projects", cut.Find("a[href='/projects']").GetAttribute("href"));
        Assert.Contains("Alpha Project", cut.Markup);
        Assert.Contains("Beta Project", cut.Markup);
        Assert.NotNull(cut.Find(".project-switcher .dropdown-toggle"));
        Assert.NotNull(cut.Find(".project-switcher a[href^='/projects/'][href$='/home']"));
    }

    private void AddLayoutServices(TestDatabase database, bool authenticated = false)
    {
        Services.AddAuthorization();
        Services.AddCascadingAuthenticationState();
        Services.AddSingleton<IAuthorizationService, AllowAuthorizationService>();
        Services.AddScoped(_ => database.CreateContext());
        Services.AddSingleton<IDbContextFactory<FeatureFlagDbContext>>(_ =>
            new TestFeatureFlagDbContextFactory(database.CreateContext));
        Services.AddSingleton<AuthenticationStateProvider>(
            authenticated
                ? new BunitAuthenticationStateProvider(
                    "owner@example.com",
                    [],
                    [new Claim(ClaimTypes.NameIdentifier, TestDatabase.UserId)],
                    "Test")
                : new BunitAuthenticationStateProvider());
    }

    private sealed class AllowAuthorizationService : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            IEnumerable<IAuthorizationRequirement> requirements)
            => Task.FromResult(AuthorizationResult.Success());

        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            string policyName)
            => Task.FromResult(AuthorizationResult.Success());
    }

    private sealed class TestFeatureFlagDbContextFactory(Func<FeatureFlagDbContext> createContext)
        : IDbContextFactory<FeatureFlagDbContext>
    {
        public FeatureFlagDbContext CreateDbContext() => createContext();
    }

    private sealed class TestDatabase : IDisposable
    {
        public const string UserId = "owner-user";

        private readonly SqliteConnection connection = new("Data Source=:memory:");

        public TestDatabase()
        {
            connection.Open();

            using var context = CreateContext();
            context.Database.EnsureCreated();
        }

        public FeatureFlagDbContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<FeatureFlagDbContext>()
                .UseSqlite(connection)
                .Options;

            return new FeatureFlagDbContext(options);
        }

        public void SeedProject(string name)
        {
            using var context = CreateContext();
            context.Projects.Add(new Project
            {
                Name = name,
                Environments =
                {
                    new ProjectEnvironment { Name = "Development" }
                },
                Members =
                {
                    new ProjectMember
                    {
                        UserId = UserId,
                        Email = "owner@example.com",
                        DisplayName = "Owner",
                        Role = ProjectRole.Owner
                    }
                }
            });
            context.SaveChanges();
        }

        public void Dispose()
        {
            connection.Dispose();
        }
    }
}
