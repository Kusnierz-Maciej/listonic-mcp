using System.Security.Cryptography;
using System.Text;

namespace ListonicMcp.Security.OAuth;

/// <summary>PKCE (RFC 7636) S256 verification. Plain is intentionally unsupported.</summary>
public static class Pkce
{
    /// <summary>True when <paramref name="codeVerifier"/> matches <paramref name="codeChallenge"/> under S256.</summary>
    public static bool VerifyS256(string? codeVerifier, string? codeChallenge)
    {
        if (string.IsNullOrEmpty(codeVerifier) || string.IsNullOrEmpty(codeChallenge))
            return false;

        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        var expected = Base64Url(hash);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(codeChallenge));
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
