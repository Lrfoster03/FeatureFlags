using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using FeatureFlags.Components.Models;
using FeatureFlags.Components.Shared;
using FeatureFlags.Data;
using FeatureFlags.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace FeatureFlags.Tests;

public class AuditHistoryTests
{
    [Fact]
    public async Task Closed_history_does_not_query_audit_events_on_render_or_project_switch()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var queries = new List<string>();
        var options = new DbContextOptionsBuilder<FeatureFlagDbContext>().UseSqlite(connection)
            .LogTo(queries.Add, [RelationalEventId.CommandExecuted]).Options;
        var factory = new TestContextFactory(() => new(options));
        await using var db = factory.CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        var first = new Project { Name = "First", Members = { new() { UserId = "viewer", Role = ProjectRole.Viewer } } };
        var second = new Project { Name = "Second", Members = { new() { UserId = "viewer", Role = ProjectRole.Viewer } } };
        var forbidden = new Project { Name = "Forbidden" };
        db.Projects.AddRange(first, second, forbidden);
        db.AuditEvents.AddRange(Entry(first.Id, DateTime.UtcNow, "flag", "1", "owner", 1),
            Entry(second.Id, DateTime.UtcNow, "config", "1", "editor", 2));
        await db.SaveSeedChangesAsync();
        var auth = new BunitAuthenticationStateProvider("Viewer", [], [new Claim(ClaimTypes.NameIdentifier, "viewer")], "Test");
        using var ui = new BunitContext();
        ui.Services.AddSingleton(new AuditHistory(factory, auth));
        ui.JSInterop.SetupModule("./Components/Shared/ProjectHistory.razor.js").Mode = JSRuntimeMode.Loose;
        queries.Clear();

        var cut = ui.Render<ProjectHistory>(p => p.Add(c => c.ProjectId, first.Id));
        cut.WaitForAssertion(() => Assert.Contains("First", cut.Find(".history-heading").TextContent));
        cut.Render(p => p.Add(c => c.ProjectId, second.Id));
        cut.WaitForAssertion(() => Assert.Contains("Second", cut.Find(".history-heading").TextContent));
        Assert.Single(cut.FindAll("select[aria-label='Who'] option"));
        Assert.Single(cut.FindAll("select[aria-label='Environment'] option"));
        cut.Render(p => p.Add(c => c.ProjectId, forbidden.Id));
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("button")));
        Assert.Contains(queries, query => query.Contains("\"Projects\""));
        Assert.DoesNotContain(queries, query => query.Contains("\"AuditEvents\""));
    }

    [Fact]
    public async Task History_is_scoped_authorized_and_paginated_with_stable_ties()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<FeatureFlagDbContext>().UseSqlite(connection).Options;
        var factory = new TestContextFactory(() => new(options));
        await using var db = factory.CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        var project = new Project { Name = "First", Members = { new() { UserId = "viewer", Email = "viewer@example.com", Role = ProjectRole.Viewer } } };
        var foreign = new Project { Name = "Other" };
        db.Projects.AddRange(project, foreign);
        await db.SaveSeedChangesAsync();
        var time = new DateTime(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);
        var first = Entry(project.Id, time, "flag", "1", "owner", 1);
        var second = Entry(project.Id, time, "config", "1", "editor", 2);
        var third = Entry(project.Id, time.AddDays(-1), "flag", "2", "owner", 1);
        var secret = Entry(foreign.Id, time, "flag", "1", "other", 3);
        db.AuditEvents.AddRange(first, second, third, secret);
        await db.SaveSeedChangesAsync();
        var auth = new BunitAuthenticationStateProvider("Viewer", [], [new Claim(ClaimTypes.NameIdentifier, "viewer")], "Test");
        var service = new AuditHistory(factory, auth);
        var page = await service.ListAsync(project.Id, new(), pageSize: 1);
        Assert.Equal(second.Id, Assert.Single(page.Events).Id);
        page = await service.ListAsync(project.Id, new(), page.Next, pageSize: 1);
        Assert.Equal(first.Id, Assert.Single(page.Events).Id);
        page = await service.ListAsync(project.Id, new(), page.Next, pageSize: 1);
        Assert.Equal(third.Id, Assert.Single(page.Events).Id);
        Assert.Null(page.Next);
        Assert.Equal(first.Id, Assert.Single((await service.ListAsync(project.Id,
            new(Type: "flag", EntityId: "1", Actor: "owner", Environment: 1, FromUtc: time, UntilUtc: time.AddDays(1)))).Events).Id);
        Assert.Empty((await service.ListAsync(project.Id, new(UntilUtc: time.AddDays(-1)))).Events);
        Assert.Empty((await service.ListAsync(project.Id, new(Actor: "other"))).Events);
        var metadata = await service.GetProjectAsync(project.Id, includeFilters: true);
        Assert.Equal("First", metadata.Name);
        Assert.Equal(2, metadata.Actors.Count);
        Assert.DoesNotContain(metadata.Environments, e => e.Id == "3");
        Assert.Equal(first.Before, (await service.DetailAsync(project.Id, first.Id)).Before);
        await Assert.ThrowsAsync<ArgumentException>(() => service.DetailAsync(project.Id, secret.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ListAsync(foreign.Id, new()));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ListAsync(project.Id, new(FromUtc: time, UntilUtc: time)));
        project.Members.Single().RevokedAt = DateTime.UtcNow;
        await db.SaveSeedChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ListAsync(project.Id, new()));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.DetailAsync(project.Id, first.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetProjectAsync(project.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetProjectAsync(project.Id, includeFilters: true));
    }

    private static AuditEvent Entry(string project, DateTime time, string type, string id, string actor, int env) => new()
    {
        OperationId = Guid.NewGuid(), ProjectId = project, OccurredAtUtc = time, EntityType = type, EntityId = id,
        ActorUserId = actor, ActorDisplayName = actor, EnvironmentId = env, EnvironmentName = $"Environment {env}",
        EntityName = "Resource", Action = type + ".updated", Before = "{\"name\":\"Before\"}", After = "{\"name\":\"After\"}"
    };

    [Theory]
    [InlineData("{}", "{\"new\":null}", "/new", "Added", "—", "null")]
    [InlineData("{\"old\":null}", "{}", "/old", "Removed", "null", "—")]
    [InlineData("{}", "{\"new\":{}}", "/new", "Added", "—", "{}")]
    [InlineData("{\"old\":{}}", "{}", "/old", "Removed", "{}", "—")]
    [InlineData("{\"value\":{\"a/b~c\":1}}", "{\"value\":{\"a/b~c\":2}}", "/value/a~1b~0c", "Changed", "1", "2")]
    [InlineData("{\"value\":[1,2]}", "{\"value\":[2,1]}", "/value", "Changed", "[1,2]", "[2,1]")]
    [InlineData("{\"percentageRollout\":10}", "{\"percentageRollout\":25}", "/percentageRollout", "Changed", "10%", "25%")]
    [InlineData("{\"isEnabled\":false}", "{\"isEnabled\":true}", "/isEnabled", "Changed", "Off", "On")]
    public void Diffs_distinguish_structure_presence_and_readable_flag_values(string before, string after, string path, string kind, string oldValue, string newValue)
    {
        Assert.Equal(new AuditFieldChange(path, kind, oldValue, newValue), Assert.Single(AuditDiff.Compare(before, after)));
    }

    [Fact]
    public void Diffs_ignore_formatting_and_key_order_but_preserve_long_text()
    {
        Assert.Empty(AuditDiff.Compare("{\"a\":1,\"b\":true}", "{ \"b\":true, \"a\":1 }"));
        var text = new string('x', 10000);
        Assert.Contains(text, Assert.Single(AuditDiff.Compare(null, $"{{\"description\":\"{text}\"}}")).After);
        Assert.Equal("No resource", AuditDiff.Pretty(null));
        Assert.Equal("null", AuditDiff.Pretty("null"));
    }
}
