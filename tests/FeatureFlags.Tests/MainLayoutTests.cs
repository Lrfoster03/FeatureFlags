using Bunit;
using FeatureFlags.Components.Layout;
using Microsoft.AspNetCore.Components;

namespace FeatureFlags.Tests;

public class MainLayoutTests : BunitContext
{
    [Fact]
    public void MainLayout_Renders_Body_And_Error_UI()
    {
        var cut = Render<LayoutView>(parameters => parameters
            .Add(p => p.Layout, typeof(MainLayout))
            .AddChildContent("<p id=\"layout-body\">Hello layout</p>"));

        Assert.Contains("Hello layout", cut.Markup);
        Assert.Equal("blazor-error-ui", cut.Find("#blazor-error-ui").Id);
        Assert.Contains("An unhandled error has occurred.", cut.Markup);
        Assert.Equal(".", cut.Find("a.reload").GetAttribute("href"));
        Assert.Contains("Reload", cut.Markup);
        Assert.Contains("🗙", cut.Markup);
    }
}
