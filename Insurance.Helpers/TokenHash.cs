using System.Security.Cryptography;
using System.Text;

namespace Insurance.Helpers;

public static class TokenHash
{
    public static string Sha256(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes);
    }

    public static string NewOpaqueToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
}
