using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FeatureFlags.Tests;

public class OpenApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory = factory.WithWebHostBuilder(builder =>
    {
        builder.UseSetting("SkipDatabaseMigrations", "true");
    });

    [Fact]
    public async Task OpenApiSpec_Is_Available_When_Startup_Migrations_Are_Skipped()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("3.1.1", document.RootElement.GetProperty("openapi").GetString());
        Assert.Equal("Feature Flags API", document.RootElement.GetProperty("info").GetProperty("title").GetString());

        var paths = document.RootElement.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/v1/featureflags", out _));
        Assert.False(paths.TryGetProperty("/api/featureflags", out _));
    }
}
