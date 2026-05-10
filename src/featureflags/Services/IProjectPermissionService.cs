using FeatureFlags.Components.Models;

namespace FeatureFlags.Services;

public interface IProjectPermissionService
{
    Task<bool> CanViewProjectAsync(string projectId, string? userId);
    Task<bool> CanEditFlagsAsync(string projectId, string? userId);
    Task<bool> CanManageMembersAsync(string projectId, string? userId);
    Task<bool> CanManageKeysAsync(string projectId, string? userId);
}