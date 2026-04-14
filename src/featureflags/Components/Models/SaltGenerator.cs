using System;
using System.Security.Cryptography;

namespace FeatureFlags.Components.Models;

// Shamelessly copied from https://www.youtube.com/watch?v=AU-4oLUV5VU&t=89s
public class SaltGenerator
{
    public static string CreateSalt(int size = 32) // 32 bytes = 256 bits
    {
        // Use RNGCryptoServiceProvider for strong randomness (or RandomNumberGenerator.GetBytes)
        byte[] saltBytes = new byte[size];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(saltBytes);
        }
        return Convert.ToBase64String(saltBytes);
    }
}
