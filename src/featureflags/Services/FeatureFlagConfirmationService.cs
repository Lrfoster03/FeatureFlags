using BlazorBootstrap;

namespace FeatureFlags.Services;

public delegate Task<bool?> ConfirmDialogInvoker(
    ConfirmDialog dialog,
    string title,
    string message1,
    string message2,
    ConfirmDialogOptions options);

public interface IFeatureFlagConfirmationService
{
    Task<bool> ConfirmDeleteAsync(ConfirmDialog dialog, string featureFlagName);
}

public sealed class FeatureFlagConfirmationService : IFeatureFlagConfirmationService
{
    private readonly ConfirmDialogInvoker showDialog;

    public FeatureFlagConfirmationService()
        : this(async (dialog, title, message1, message2, options) =>
            await dialog.ShowAsync(title, message1, message2, options))
    {
    }

    public FeatureFlagConfirmationService(ConfirmDialogInvoker showDialog)
    {
        this.showDialog = showDialog;
    }

    public async Task<bool> ConfirmDeleteAsync(ConfirmDialog dialog, string featureFlagName)
    {
        var options = new ConfirmDialogOptions
        {
            YesButtonText = "Delete",
            YesButtonColor = ButtonColor.Danger,
            NoButtonText = "Cancel",
            NoButtonColor = ButtonColor.Secondary
        };

        var confirmed = await showDialog(
            dialog,
            "Delete Feature Flag",
            $"Are you sure you want to delete '{featureFlagName}'?",
            "This action cannot be undone.",
            options);

        return confirmed == true;
    }
}
