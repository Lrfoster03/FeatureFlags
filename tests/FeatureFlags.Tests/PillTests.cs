using Bunit;
using Xunit;
using FeatureFlags.Components.Shared;
using FeatureFlags.Components.Models;
using System.Text.Json.Nodes;

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
            PercentageRollout = 100
        };

        var cut = Render<FlagPill>(parameters => parameters
            .Add(p => p.FeatureFlag, flag));

        Assert.Contains("MyTestFlag", cut.Markup);
        Assert.Contains("100%", cut.Markup);
    }
    
    [Fact]
    public void Pill_Delete_Click_Invokes_OnDelete()
    {
        var flag = new FeatureFlag
        {
            Id = 1,
            Name = "MyFlag",
            Description = "My description",
            PercentageRollout = 0
        };

        FeatureFlag? deleted = null;

        var cut = Render<FlagPill>(ps => ps
            .Add(p => p.FeatureFlag, flag)
            .Add(p => p.OnDelete, f => deleted = f));

        cut.Find(".pill-header").Click();
        cut.Find("button").Click();

        Assert.NotNull(deleted);
        Assert.Equal(flag.Id, deleted!.Id);
    }

    [Fact]
    public void ConfigPill_Renders_And_Updates_Json_Value()
    {
        var config = new FeatureConfig
        {
            Id = 1,
            Name = "CheckoutConfig",
            Description = "Checkout settings",
            Value = new JsonObject
            {
                ["enabled"] = true
            }
        };

        FeatureConfig? updated = null;

        var cut = Render<ConfigPill>(parameters => parameters
            .Add(p => p.Config, config)
            .Add(p => p.OnChanged, c => updated = c));

        cut.Find(".pill-header").Click();
        cut.Find("textarea.config-value-input").Input("""{"enabled":false,"limit":5}""");

        Assert.NotNull(updated);
        Assert.False(updated!.Value["enabled"]!.GetValue<bool>());
        Assert.Equal(5, updated.Value["limit"]!.GetValue<int>());
        Assert.DoesNotContain("Invalid JSON", cut.Markup);
    }

    [Fact]
    public void ConfigPill_Rejects_Invalid_Json_Value()
    {
        var config = new FeatureConfig
        {
            Id = 1,
            Name = "CheckoutConfig",
            Value = new JsonObject
            {
                ["enabled"] = true
            }
        };

        var changeCount = 0;

        var cut = Render<ConfigPill>(parameters => parameters
            .Add(p => p.Config, config)
            .Add(p => p.OnChanged, _ => changeCount++));

        cut.Find(".pill-header").Click();
        cut.Find("textarea.config-value-input").Input("""{"enabled":""");

        Assert.Equal(0, changeCount);
        Assert.True(config.Value["enabled"]!.GetValue<bool>());
        Assert.Contains("Invalid JSON", cut.Markup);
    }

    [Fact]
    public void ConfigPill_Format_And_Minify_Json_Value()
    {
        var config = new FeatureConfig
        {
            Id = 1,
            Name = "CheckoutConfig",
            Value = new JsonObject
            {
                ["enabled"] = true,
                ["limit"] = 5
            }
        };

        var cut = Render<ConfigPill>(parameters => parameters
            .Add(p => p.Config, config));

        cut.Find(".pill-header").Click();
        var editor = cut.Find("textarea.config-value-input");

        editor.Input("""{"enabled":false,"limit":10}""");
        cut.Find("button.value-format-button").Click();

        Assert.Contains(Environment.NewLine, GetEditorText(cut));
        Assert.Contains("\"enabled\": false", GetEditorText(cut));

        cut.Find("button.value-minify-button").Click();

        Assert.Equal("""{"enabled":false,"limit":10}""", GetEditorText(cut));
    }

    [Fact]
    public void ConfigPill_Rejects_Value_That_Does_Not_Match_Schema()
    {
        var config = new FeatureConfig
        {
            Id = 1,
            Name = "CheckoutConfig",
            Schema = new JsonObject
            {
                ["type"] = "object",
                ["required"] = new JsonArray("enabled"),
                ["properties"] = new JsonObject
                {
                    ["enabled"] = new JsonObject { ["type"] = "boolean" }
                },
                ["additionalProperties"] = false
            },
            Value = new JsonObject
            {
                ["enabled"] = true
            }
        };

        var changeCount = 0;

        var cut = Render<ConfigPill>(parameters => parameters
            .Add(p => p.Config, config)
            .Add(p => p.OnChanged, _ => changeCount++));

        cut.Find(".pill-header").Click();
        cut.Find("textarea.config-value-input").Input("""{"enabled":"yes"}""");

        Assert.Equal(0, changeCount);
        Assert.True(config.Value["enabled"]!.GetValue<bool>());
        Assert.Contains("$.enabled must be boolean.", cut.Markup);
    }

    [Fact]
    public void ConfigPill_Updates_Schema_When_Current_Value_Matches()
    {
        var config = new FeatureConfig
        {
            Id = 1,
            Name = "CheckoutConfig",
            Value = new JsonObject
            {
                ["enabled"] = true
            }
        };

        FeatureConfig? updated = null;

        var cut = Render<ConfigPill>(parameters => parameters
            .Add(p => p.Config, config)
            .Add(p => p.OnChanged, c => updated = c));

        cut.Find(".pill-header").Click();
        cut.Find("textarea.config-schema-input").Input("""
        {
          "type": "object",
          "required": ["enabled"],
          "properties": {
            "enabled": { "type": "boolean" }
          }
        }
        """);

        Assert.NotNull(updated);
        Assert.Equal("object", updated!.Schema["type"]!.GetValue<string>());
        Assert.DoesNotContain("must be", cut.Markup);
    }

    private static string GetEditorText(IRenderedComponent<ConfigPill> cut)
    {
        var editor = cut.Find("textarea.config-value-input");
        return editor.GetAttribute("value") ?? editor.TextContent;
    }
}
