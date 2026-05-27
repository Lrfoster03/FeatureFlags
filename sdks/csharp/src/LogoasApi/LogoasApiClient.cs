using LogoasApi.Core;

namespace LogoasApi;

public partial class LogoasApiClient : ILogoasApiClient
{
    private readonly RawClient _client;

    public LogoasApiClient(
        string? apiKey = null,
        string? user = null,
        ClientOptions? clientOptions = null
    )
    {
        clientOptions ??= new ClientOptions();
        var platformHeaders = new Headers(
            new Dictionary<string, string>()
            {
                { "X-Fern-Language", "C#" },
                { "X-Fern-SDK-Name", "LogoasApi" },
                { "X-Fern-SDK-Version", Version.Current },
            }
        );
        foreach (var header in platformHeaders)
        {
            if (!clientOptions.Headers.ContainsKey(header.Key))
            {
                clientOptions.Headers[header.Key] = header.Value;
            }
        }
        var clientOptionsWithAuth = clientOptions.Clone();
        var authHeaders = new Headers(
            new Dictionary<string, string>()
            {
                { "X-API-Key", apiKey ?? "" },
                { "user", user ?? "" },
            }
        );
        foreach (var header in authHeaders)
        {
            clientOptionsWithAuth.Headers[header.Key] = header.Value;
        }
        _client = new RawClient(clientOptionsWithAuth);
        FeatureFlags = new FeatureFlagsClient(_client);
    }

    public IFeatureFlagsClient FeatureFlags { get; }
}
