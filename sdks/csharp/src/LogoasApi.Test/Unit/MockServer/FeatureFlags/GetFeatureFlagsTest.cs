using LogoasApi.Test.Unit.MockServer;
using LogoasApi.Test.Utils;
using NUnit.Framework;

namespace LogoasApi.Test.Unit.MockServer.FeatureFlags;

[TestFixture]
[Parallelizable(ParallelScope.Self)]
public class GetFeatureFlagsTest : BaseMockServerTest
{
    [NUnit.Framework.Test]
    public async Task MockServerTest()
    {
        const string mockResponse = """
            {
              "featureFlags": {
                "NewUI": true,
                "BetaCheckout": false
              },
              "configs": {
                "Theme": {
                  "color": "blue",
                  "layout": "compact"
                },
                "Checkout": {
                  "maxItems": 10,
                  "allowCoupons": true
                }
              }
            }
            """;

        Server
            .Given(
                WireMock.RequestBuilders.Request.Create().WithPath("/api/featureflags").UsingGet()
            )
            .RespondWith(
                WireMock
                    .ResponseBuilders.Response.Create()
                    .WithStatusCode(200)
                    .WithBody(mockResponse)
            );

        var response = await Client.FeatureFlags.GetFeatureFlagsAsync();
        JsonAssert.AreEqual(response, mockResponse);
    }
}
