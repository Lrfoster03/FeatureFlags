using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FeatureFlags.Tests;

public class RateLimitTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("SkipDatabaseMigrations", "true");
        });

    [Fact]
    public async Task ApiFeatureFlags_Returns429_WhenAnonymousLimitExceeded()
    {
        var client = factory.CreateClient();

        for (var i = 0; i < 10; i++)
        {
            using var allowedRequest = new HttpRequestMessage(HttpMethod.Get, "/api/featureflags");
            var allowedResponse = await client.SendAsync(allowedRequest);

            Assert.Equal(HttpStatusCode.Unauthorized, allowedResponse.StatusCode);
        }

        using var limitedRequest = new HttpRequestMessage(HttpMethod.Get, "/api/featureflags");
        var limited = await client.SendAsync(limitedRequest);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }
}
