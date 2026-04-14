using System.Security.Cryptography;
using System.Text;
using FeatureFlags.Components.Models;

namespace FeatureFlags.Core;

public static class Evaluator
{
    public static FeatureFlagsResponse Evaluate(IEnumerable<FeatureFlag> flags, string? userId)
    {
        var response = new FeatureFlagsResponse
        {
            FeatureFlags = flags.ToDictionary(
                flag => flag.Name,
                flag => EvaluateFlag(flag, userId))
        };

        return response;
    }

    public static bool EvaluateFlag(FeatureFlag flag, string? userId)
    {
        userId ??= string.Empty;

        if (flag.PercentageRollout == 0)
            return false;

        if (flag.PercentageRollout == 100)
            return true;

        using var md5 = MD5.Create();
        
        // 1. Combine userId and salt
        string bucket = userId + flag.Salt;
        byte[] hashBytes = MD5.HashData(Encoding.UTF8.GetBytes(bucket));

        // 2. Convert first 4 bytes to an integer
        uint hashNumber = BitConverter.ToUInt32(hashBytes, 0);

        // 3. Map to bucket (e.g., 100 for 1%) and check against rollout percentage
        return (int)(hashNumber % 100) < flag.PercentageRollout;
    }
}