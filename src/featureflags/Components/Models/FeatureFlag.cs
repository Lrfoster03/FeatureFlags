namespace FeatureFlags.Components.Models;

public class FeatureFlag
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }

    private readonly string? _salt;
    public string Salt { get; }

    public FeatureFlag()
    {
        _salt = SaltGenerator.CreateSalt();
        Salt = _salt;
    }

    public FeatureFlag(string name, string description = "", bool isEnabled = false)
    {
        Name = name;
        Description = description;
        IsEnabled = isEnabled;
        _salt = SaltGenerator.CreateSalt();
        Salt = _salt;
    }
}