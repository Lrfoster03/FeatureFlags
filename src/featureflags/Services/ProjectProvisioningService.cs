using FeatureFlags.Components.Models;
using FeatureFlags.Data;

namespace FeatureFlags.Services;

public sealed class ProjectProvisioningService(FeatureFlagDbContext db) : IProjectProvisioningService
{
    private readonly FeatureFlagDbContext _db = db;

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

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        _db.Projects.Add(project);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return project;
    }
}
