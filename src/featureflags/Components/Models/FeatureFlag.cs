namespace FeatureFlags.Components.Models;

public class FeatureFlag
{
    public int Id { get; set; }

    public int ProjectEnvironmentId { get; set; }
    public ProjectEnvironment ProjectEnvironment { get; set; } = default!;

    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public bool IsEnabled { get; set; }
    public int PercentageRollout { get; set; } = 0; // 0-100 for percentage rollout
    public string Salt { get; set; } = SaltGenerator.CreateSalt();

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