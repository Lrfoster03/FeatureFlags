using Bunit;
using Xunit;
using FeatureFlags.Components.Shared;
using FeatureFlags.Components.Models;

namespace FeatureFlags.Tests;

public class PillTests : BunitContext
{
    [Fact]
    public void Pill_Renders_FeatureFlag_Name()
    {
        var flag = new FeatureFlag
        {
            Name = "MyTestFlag",
            Description = "Test description",
            IsEnabled = true
        };

        var cut = Render<Pill>(parameters => parameters
            .Add(p => p.FeatureFlag, flag));

        Assert.Contains("MyTestFlag", cut.Markup);
    }
    
    [Fact]
    public void Pill_Delete_Click_Invokes_OnDelete()
    {
        var flag = new FeatureFlag
        {
            Id = 1,
            Name = "MyFlag",
            Description = "My description",
            IsEnabled = false
        };

        FeatureFlag? deleted = null;

        var cut = Render<Pill>(ps => ps
            .Add(p => p.FeatureFlag, flag)
            .Add(p => p.OnDelete, f => deleted = f));

        cut.Find(".pill-header").Click();
        cut.Find("button[title='Delete feature flag'], button").Click();

        Assert.NotNull(deleted);
        Assert.Equal(flag.Id, deleted!.Id);
    }
}