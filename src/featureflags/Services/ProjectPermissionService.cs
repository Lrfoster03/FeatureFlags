using FeatureFlags.Components.Models;
using FeatureFlags.Data;
using Microsoft.EntityFrameworkCore;

namespace FeatureFlags.Services;

public class ProjectPermissionService(FeatureFlagDbContext db) : IProjectPermissionService
{
    public Task<bool> CanViewProjectAsync(string projectId, string? userId)
        => HasRole(projectId, userId, ProjectRole.Viewer, ProjectRole.Editor, ProjectRole.Admin, ProjectRole.Owner);

    public Task<bool> CanEditFlagsAsync(string projectId, string? userId)
        => HasRole(projectId, userId, ProjectRole.Editor, ProjectRole.Admin, ProjectRole.Owner);

    public Task<bool> CanManageMembersAsync(string projectId, string? userId)
        => HasRole(projectId, userId, ProjectRole.Admin, ProjectRole.Owner);

    public Task<bool> CanManageKeysAsync(string projectId, string? userId)
        => HasRole(projectId, userId, ProjectRole.Admin, ProjectRole.Owner);

    private Task<bool> HasRole(string projectId, string? userId, params ProjectRole[] roles)
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