using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ListonicMcp.Security.OAuth;

/// <summary>The kind of token a payload belongs to — bound into the payload so a code can't be used as
/// an access token, a refresh token can't be replayed at <c>/mcp</c>, etc.</summary>
public enum TokenKind
{
    Code,
    Access,
    Refresh,
}

/// <summary>
/// The decrypted contents of an OAuth token. Carries the Listonic credentials, a salted hash of the API key
/// (for the allowlist kill-switch), the token kind, and PKCE/redirect binding (codes only).
/// </summary>
public sealed record TokenPayload(
    TokenKind Kind,
    string Email,
    string Password,
    string ApiKeyHash,
    string? CodeChallenge,
    string? RedirectUri);

/// <summary>
/// Encrypts/decrypts opaque OAuth tokens with AES-256-GCM. Tokens are not readable JWTs: the whole payload
/// (incl. the Listonic password) is authenticated-encrypted with an embedded expiry. The AES key is derived
/// deterministically from <c>Security:TokenSecret</c> via HKDF-SHA256 — nothing is persisted to disk, so
/// tokens survive restarts and are portable across replicas, and changing the secret invalidates every
/// issued token. The token kind is bound as additional authenticated data so a blob protected as one kind
/// cannot be unprotected as another.
/// </summary>
public sealed class CredentialTokenService
{
    private const int KeySize = 32;     // AES-256
    private const int NonceSize = 12;   // AES-GCM standard nonce
    private const int TagSize = 16;     // AES-GCM tag

    // Fixed, app-specific HKDF salt and info. The secret is the entropy; these just domain-separate this
    // key from any other HKDF use of the same secret.
    private static readonly byte[] HkdfSalt = "ListonicMcp.TokenSecret.v1"u8.ToArray();
    private static readonly byte[] HkdfInfo = "ListonicMcp.OAuth.TokenKey"u8.ToArray();

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly byte[] _key;

    public CredentialTokenService(string tokenSecret)
    {
        _key = HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            ikm: Encoding.UTF8.GetBytes(tokenSecret),
            outputLength: KeySize,
            salt: HkdfSalt,
            info: HkdfInfo);
    }

    /// <summary>Protects a payload into an opaque token string valid for <paramref name="ttl"/>.</summary>
    public string Protect(TokenPayload payload, TimeSpan ttl)
    {
        // Layout (before encryption): [expiryUnixSeconds: 8 bytes big-endian][payload JSON].
        var json = JsonSerializer.SerializeToUtf8Bytes(payload, Json);
        var plaintext = new byte[sizeof(long) + json.Length];
        BinaryPrimitives.WriteInt64BigEndian(plaintext, DateTimeOffset.UtcNow.Add(ttl).ToUnixTimeSeconds());
        json.CopyTo(plaintext.AsSpan(sizeof(long)));

        // Token = [nonce][tag][ciphertext]. The kind is authenticated as AAD, not stored in the clear.
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        using var gcm = new AesGcm(_key, TagSize);
        gcm.Encrypt(nonce, plaintext, ciphertext, tag, Aad(payload.Kind));

        var token = new byte[NonceSize + TagSize + ciphertext.Length];
        nonce.CopyTo(token.AsSpan(0));
        tag.CopyTo(token.AsSpan(NonceSize));
        ciphertext.CopyTo(token.AsSpan(NonceSize + TagSize));
        return Base64Url.Encode(token);
    }

    /// <summary>
    /// Unprotects a token and returns its payload, or null if the token is tampered, expired, of the
    /// wrong kind, or otherwise invalid. Never throws for an invalid token.
    /// </summary>
    public TokenPayload? TryUnprotect(string? token, TokenKind expectedKind)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        try
        {
            var raw = Base64Url.Decode(token);
            if (raw.Length < NonceSize + TagSize + sizeof(long))
                return null;

            var nonce = raw.AsSpan(0, NonceSize);
            var tag = raw.AsSpan(NonceSize, TagSize);
            var ciphertext = raw.AsSpan(NonceSize + TagSize);
            var plaintext = new byte[ciphertext.Length];

            using var gcm = new AesGcm(_key, TagSize);
            // AAD binds the expected kind: a token minted as another kind fails authentication here.
            gcm.Decrypt(nonce, ciphertext, tag, plaintext, Aad(expectedKind));

            var expiry = BinaryPrimitives.ReadInt64BigEndian(plaintext);
            if (DateTimeOffset.FromUnixTimeSeconds(expiry) < DateTimeOffset.UtcNow)
                return null; // expired

            var payload = JsonSerializer.Deserialize<TokenPayload>(plaintext.AsSpan(sizeof(long)), Json);

            // Defence in depth: the payload's own Kind must match what we expected to unprotect.
            return payload is { } p && p.Kind == expectedKind ? p : null;
        }
        catch (CryptographicException)
        {
            return null; // tampered, wrong key, or wrong kind (AAD mismatch)
        }
        catch (FormatException)
        {
            return null; // not a valid token string
        }
        catch (JsonException)
        {
            return null; // payload shape changed / corrupt
        }
    }

    /// <summary>A salted, stable hash of an API key for embedding in tokens (never the raw key).</summary>
    public static string HashApiKey(string apiKey)
    {
        // Static salt tying the hash to this application; the key itself is the secret, this just avoids
        // storing it verbatim inside tokens.
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes("ListonicMcp.ApiKey\u0000" + apiKey));
        return Convert.ToHexString(bytes);
    }

    // Per-kind additional authenticated data for domain separation between code/access/refresh tokens.
    private static byte[] Aad(TokenKind kind) => Encoding.UTF8.GetBytes($"ListonicMcp.OAuth.{kind}");
}

/// <summary>URL-safe base64 (RFC 4648 §5) without padding, for opaque token strings.</summary>
internal static class Base64Url
{
    public static string Encode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] Decode(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }
}
