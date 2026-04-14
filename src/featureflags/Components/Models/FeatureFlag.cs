namespace FeatureFlags.Components.Models;

public class FeatureFlag
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public string Salt { get; set; } = SaltGenerator.CreateSalt();


    public int PercentageRollout { get; set; } = 0; // 0-100 for percentage rollout

    public FeatureFlag()
    {
        Salt = SaltGenerator.CreateSalt();
        PercentageRollout = 0;
    }

    public FeatureFlag(string name, string description = "")
    {
        Name = name;
        Description = description;
        Salt = SaltGenerator.CreateSalt();
        PercentageRollout = 0;
    }
}