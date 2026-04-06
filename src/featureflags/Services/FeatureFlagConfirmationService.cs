using BlazorBootstrap;

namespace FeatureFlags.Services;

public interface IFeatureFlagConfirmationService
{
    Task<bool> ConfirmDeleteAsync(ConfirmDialog dialog, string featureFlagName);
}

public sealed class FeatureFlagConfirmationService : IFeatureFlagConfirmationService
{
    public async Task<bool> ConfirmDeleteAsync(ConfirmDialog dialog, string featureFlagName)
    {
        var confirmed = await dialog.ShowAsync(
            title: "Delete Feature Flag",
            message1: $"Are you sure you want to delete '{featureFlagName}'?",
            message2: "This action cannot be undone.",
            confirmDialogOptions: new ConfirmDialogOptions
            {
                YesButtonText = "Delete",
                YesButtonColor = ButtonColor.Danger,
                NoButtonText = "Cancel",
                NoButtonColor = ButtonColor.Secondary
            });

        return confirmed == true;
    }
}
