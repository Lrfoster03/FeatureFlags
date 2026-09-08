using FeatureFlags.Components.Models;
using FeatureFlags.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Components.Authorization;

namespace FeatureFlags.Services;

public sealed class ProjectProvisioningService(IDbContextFactory<FeatureFlagDbContext> dbFactory, AuthenticationStateProvider authentication)
    : ProjectMutation(dbFactory, authentication), IProjectProvisioningService
{
    public async Task<Project> CreateProjectForUserAsync(
        ApplicationUser user,
        string? projectName = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(user.Id))
            throw new ArgumentException("User must have an id before a project can be created.", nameof(user));

        var displayName = user.UserName ?? user.Email ?? "User";
        var name = string.IsNullOrWhiteSpace(projectName)
            ? $"{displayName}'s Project"
            : projectName.Trim();

        var project = new Project
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
                    UserId = user.Id,
                    Email = user.Email ?? user.UserName ?? string.Empty,
                    DisplayName = displayName,
                    Role = ProjectRole.Owner
                }
            }
        };

        project.Name = ProjectChanges.ValidateName(project.Name);
        await ExecuteAsync(project.Id, Guid.NewGuid(), ProjectRole.Owner, context =>
        {
            if (context.ActorId != user.Id) throw new UnauthorizedAccessException("Create a project as the signed-in user.");
            context.Db.Projects.Add(project);
            context.Record(project, "project.created");
            return Task.CompletedTask;
        }, creatingProject: true, cancellationToken);

        return project;
    }
}
