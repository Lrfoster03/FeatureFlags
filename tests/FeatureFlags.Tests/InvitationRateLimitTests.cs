using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;

namespace FeatureFlags.Tests;

public class InvitationRateLimitTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Only_trusted_forwarders_get_independent_client_buckets(bool trusted)
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("SkipDatabaseMigrations", "true");
            builder.UseSetting("ReverseProxy:KnownProxies:0", "10.44.0.2");
        });
        using var client = factory.CreateClient(); // Start the application's actual middleware pipeline.
        var proxy = trusted ? "10.44.0.2" : "10.44.0.3";
        for (var i = 0; i < 30; i++)
        {
            var response = await Send(factory, proxy, "192.0.2.10", i % 2 == 0);
            Assert.Equal(StatusCodes.Status200OK, response.Response.StatusCode);
        }
        Assert.Equal(StatusCodes.Status429TooManyRequests, (await Send(factory, proxy, "192.0.2.10", true)).Response.StatusCode);
        var other = await Send(factory, proxy, "192.0.2.11", true);
        Assert.Equal(trusted ? StatusCodes.Status200OK : StatusCodes.Status429TooManyRequests, other.Response.StatusCode);
        Assert.Equal(trusted ? "192.0.2.11" : proxy, other.Connection.RemoteIpAddress!.ToString());
        Assert.Equal(trusted ? "https" : "http", other.Request.Scheme);
        // Registration POSTs consume the same bucket even if the token is supplied in the form body.
        var post = await factory.Server.SendAsync(context =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(proxy);
            context.Request.Headers["X-Forwarded-For"] = "192.0.2.10";
            context.Request.Method = "POST";
            context.Request.Scheme = "http";
            context.Request.Host = new HostString("localhost");
            context.Request.Path = "/Identity/Account/Register";
        });
        Assert.Equal(StatusCodes.Status429TooManyRequests, post.Response.StatusCode);
    }

    private static Task<HttpContext> Send(WebApplicationFactory<Program> factory, string proxy, string client, bool register)
        => factory.Server.SendAsync(context =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(proxy);
            context.Request.Method = "GET";
            context.Request.Scheme = "http";
            context.Request.Host = new HostString("localhost");
            context.Request.Path = register ? "/Identity/Account/Register" : "/invitations/accept";
            context.Request.QueryString = new QueryString(register ? "?invitationToken=invalid" : "?token=invalid");
            context.Request.Headers["X-Forwarded-For"] = client;
            context.Request.Headers["X-Forwarded-Proto"] = "https";
        });
}
