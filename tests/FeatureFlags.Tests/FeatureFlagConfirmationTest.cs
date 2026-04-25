using BlazorBootstrap;
using FeatureFlags.Services;

namespace FeatureFlags.Tests;

public class FeatureFlagConfirmationTest
{
    [Fact]
    public async Task ConfirmDeleteAsync_Returns_True_When_Dialog_Confirms()
    {
        ConfirmDialog? capturedDialog = null;
        string? capturedTitle = null;
        string? capturedMessage1 = null;
        string? capturedMessage2 = null;
        ConfirmDialogOptions? capturedOptions = null;

        var service = new FeatureFlagConfirmationService(
            (dialog, title, message1, message2, options) =>
            {
                capturedDialog = dialog;
                capturedTitle = title;
                capturedMessage1 = message1;
                capturedMessage2 = message2;
                capturedOptions = options;
                return Task.FromResult<bool?>(true);
            });

        var result = await service.ConfirmDeleteAsync(default!, "Alpha");

        Assert.True(result);
        Assert.Equal("Delete Feature Flag", capturedTitle);
        Assert.Equal("Are you sure you want to delete 'Alpha'?", capturedMessage1);
        Assert.Equal("This action cannot be undone.", capturedMessage2);
        Assert.NotNull(capturedOptions);
        Assert.Equal("Delete", capturedOptions!.YesButtonText);
        Assert.Equal(ButtonColor.Danger, capturedOptions.YesButtonColor);
        Assert.Equal("Cancel", capturedOptions.NoButtonText);
        Assert.Equal(ButtonColor.Secondary, capturedOptions.NoButtonColor);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public async Task ConfirmDeleteAsync_Returns_False_When_Dialog_Does_Not_Confirm(bool? dialogResult)
    {
        var service = new FeatureFlagConfirmationService(
            (_, _, _, _, _) => Task.FromResult(dialogResult));

        var result = await service.ConfirmDeleteAsync(default!, "Alpha");

        Assert.False(result);
    }
}
