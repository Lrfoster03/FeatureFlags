using FeatureFlags.Components.Models;
using FeatureFlags.Data;
using Microsoft.EntityFrameworkCore;

namespace FeatureFlags.Services;

public class ProjectPermissionService(FeatureFlagDbContext db) : IProjectPermissionService
{
    public Task<bool> CanViewProjectAsync(int projectId, string? userId)
        => HasRole(projectId, userId, ProjectRole.Viewer, ProjectRole.Editor, ProjectRole.Admin, ProjectRole.Owner);

    public Task<bool> CanEditFlagsAsync(int projectId, string? userId)
        => HasRole(projectId, userId, ProjectRole.Editor, ProjectRole.Admin, ProjectRole.Owner);

    public Task<bool> CanManageMembersAsync(int projectId, string? userId)
        => HasRole(projectId, userId, ProjectRole.Admin, ProjectRole.Owner);

    public Task<bool> CanManageKeysAsync(int projectId, string? userId)
        => HasRole(projectId, userId, ProjectRole.Admin, ProjectRole.Owner);

    private Task<bool> HasRole(int projectId, string? userId, params ProjectRole[] roles)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return Task.FromResult(false);

        return db.ProjectMembers.AnyAsync(m =>
            m.ProjectId == projectId &&
            m.UserId == userId &&
            roles.Contains(m.Role) &&
            m.RevokedAt == null);
    }
}