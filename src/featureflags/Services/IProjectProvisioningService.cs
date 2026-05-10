using FeatureFlags.Components.Models;
using FeatureFlags.Data;

namespace FeatureFlags.Services;

public interface IProjectProvisioningService
{
    Task<Project> CreateProjectForUserAsync(
        ApplicationUser user,
        string? projectName = null,
        CancellationToken cancellationToken = default);
}
