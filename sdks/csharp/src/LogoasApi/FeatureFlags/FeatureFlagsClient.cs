using global::System.Text.Json;
using LogoasApi.Core;

namespace LogoasApi;

public partial class FeatureFlagsClient : IFeatureFlagsClient
{
    private readonly RawClient _client;

    internal FeatureFlagsClient(RawClient client)
    {
        _client = client;
    }

    private async Task<WithRawResponse<FeatureFlagsResponse>> GetFeatureFlagsAsyncCore(
        RequestOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        var _headers = await new LogoasApi.Core.HeadersBuilder.Builder()
            .Add(_client.Options.Headers)
            .Add(_client.Options.AdditionalHeaders)
            .Add(options?.AdditionalHeaders)
            .BuildAsync()
            .ConfigureAwait(false);
        var response = await _client
            .SendRequestAsync(
                new JsonRequest
                {
                    Method = HttpMethod.Get,
                    Path = "api/featureflags",
                    Headers = _headers,
                    Options = options,
                },
                cancellationToken
            )
            .ConfigureAwait(false);
        if (response.StatusCode is >= 200 and < 400)
        {
            var responseBody = await response
                .Raw.Content.ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);
            try
            {
                var responseData = JsonUtils.Deserialize<FeatureFlagsResponse>(responseBody)!;
                return new WithRawResponse<FeatureFlagsResponse>()
                {
                    Data = responseData,
                    RawResponse = new RawResponse()
                    {
                        StatusCode = response.Raw.StatusCode,
                        Url = response.Raw.RequestMessage?.RequestUri ?? new Uri("about:blank"),
                        Headers = ResponseHeaders.FromHttpResponseMessage(response.Raw),
                    },
                };
            }
            catch (JsonException e)
            {
                throw new LogoasApiApiException(
                    "Failed to deserialize response",
                    response.StatusCode,
                    responseBody,
                    e
                );
            }
        }
        {
            var responseBody = await response
                .Raw.Content.ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);
            try
            {
                switch (response.StatusCode)
                {
                    case 401:
                        throw new UnauthorizedError(JsonUtils.Deserialize<object>(responseBody));
                }
            }
            catch (JsonException)
            {
                // unable to map error response, throwing generic error
            }
            throw new LogoasApiApiException(
                $"Error with status code {response.StatusCode}",
                response.StatusCode,
                responseBody
            );
        }
    }

    /// <summary>
    /// Returns all feature flags and configs for the project environment associated with the provided client API key. Feature flag values are evaluated for the optional user identifier.
    /// </summary>
    /// <example><code>
    /// await client.FeatureFlags.GetFeatureFlagsAsync();
    /// </code></example>
    public WithRawResponseTask<FeatureFlagsResponse> GetFeatureFlagsAsync(
        RequestOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        return new WithRawResponseTask<FeatureFlagsResponse>(
            GetFeatureFlagsAsyncCore(options, cancellationToken)
        );
    }
}
