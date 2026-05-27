namespace LogoasApi;

public partial interface ILogoasApiClient
{
    public IFeatureFlagsClient FeatureFlags { get; }
}
