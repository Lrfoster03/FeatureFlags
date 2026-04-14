namespace FeatureFlags.Components.Models;

public class FeatureFlag
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }

    private readonly string? _salt;
    public string Salt { get; }

    public int PercentageRollout { get; set; } = 0; // 0-100 for percentage rollout

    public FeatureFlag()
    {
        _salt = SaltGenerator.CreateSalt();
        Salt = _salt;
        PercentageRollout = 0;
    }

    public FeatureFlag(string name, string description = "", bool isEnabled = false)
    {
        Name = name;
        Description = description;
        IsEnabled = isEnabled;
        _salt = SaltGenerator.CreateSalt();
        Salt = _salt;
        PercentageRollout = 0;
    }
}