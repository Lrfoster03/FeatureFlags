using FeatureFlags.Components.Models;
using FeatureFlags.Data;
using Microsoft.EntityFrameworkCore;

namespace FeatureFlags.Services;

public sealed class ProjectProvisioningService(IDbContextFactory<FeatureFlagDbContext> dbFactory) : IProjectProvisioningService
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

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        db.Projects.Add(project);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return project;
    }
}
