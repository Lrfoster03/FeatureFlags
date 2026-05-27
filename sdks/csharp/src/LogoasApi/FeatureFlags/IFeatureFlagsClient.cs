namespace LogoasApi;

public partial interface IFeatureFlagsClient
{
    /// <summary>
    /// Returns all feature flags and configs for the project environment associated with the provided client API key. Feature flag values are evaluated for the optional user identifier.
    /// </summary>
    WithRawResponseTask<FeatureFlagsResponse> GetFeatureFlagsAsync(
        RequestOptions? options = null,
        CancellationToken cancellationToken = default
    );
}
