using FeatureFlags.Components.Models;

namespace FeatureFlags.Services;

public interface IProjectPermissionService
{
    Task<bool> CanViewProjectAsync(int projectId, string? userId);
    Task<bool> CanEditFlagsAsync(int projectId, string? userId);
    Task<bool> CanManageMembersAsync(int projectId, string? userId);
    Task<bool> CanManageKeysAsync(int projectId, string? userId);
}