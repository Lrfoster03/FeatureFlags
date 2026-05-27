using global::System.Text.Json;
using global::System.Text.Json.Serialization;
using LogoasApi.Core;

namespace LogoasApi;

/// <summary>
/// Evaluated feature flags and dynamic configs.
/// </summary>
[Serializable]
public record FeatureFlagsResponse : IJsonOnDeserialized
{
    [JsonExtensionData]
    private readonly IDictionary<string, JsonElement> _extensionData =
        new Dictionary<string, JsonElement>();

    /// <summary>
    /// Map of feature flag names to evaluated boolean values.
    /// </summary>
    [JsonPropertyName("featureFlags")]
    public Dictionary<string, bool> FeatureFlags { get; set; } = new Dictionary<string, bool>();

    /// <summary>
    /// Map of config names to arbitrary JSON objects.
    /// </summary>
    [JsonPropertyName("configs")]
    public Dictionary<string, Dictionary<string, object?>> Configs { get; set; } =
        new Dictionary<string, Dictionary<string, object?>>();

    [JsonIgnore]
    public ReadOnlyAdditionalProperties AdditionalProperties { get; private set; } = new();

    void IJsonOnDeserialized.OnDeserialized() =>
        AdditionalProperties.CopyFromExtensionData(_extensionData);

    /// <inheritdoc />
    public override string ToString()
    {
        return JsonUtils.Serialize(this);
    }
}
