using System.Security.Cryptography;
using System.Text;
using ListonicMcp.Security.OAuth;
using Microsoft.Extensions.Options;

namespace ListonicMcp.Security;

/// <summary>Binds the <c>Security</c> section.</summary>
public sealed class SecurityOptions
{
    public const string Section = "Security";

    /// <summary>MCP server API keys allowed to use the server. Add one per user/client.</summary>
    public string[] AllowedApiKeys { get; set; } = [];

    /// <summary>Secret seeding OAuth token encryption. Must be set, long and stable.</summary>
    public string? TokenSecret { get; set; }

    /// <summary>
    /// Whether callers must present an allowlisted MCP server API key. Default <c>true</c>. Set to
    /// <c>false</c> for trusted/private deployments to drop the API-key gate entirely (Listonic
    /// credentials are still required — the server has no default account).
    /// </summary>
    public bool RequireMcpApiKey { get; set; } = true;

    /// <summary>All configured keys, empty entries removed.</summary>
    public IEnumerable<string> AllKeys =>
        AllowedApiKeys.Where(k => !string.IsNullOrWhiteSpace(k));
}

/// <summary>
/// Startup rules for <see cref="SecurityOptions"/> (web host only — stdio has no OAuth or API key). The token
/// secret is the sole key for OAuth token encryption: a missing, placeholder or short secret would let anyone
/// forge session tokens carrying arbitrary Listonic credentials.
/// </summary>
public sealed class SecurityOptionsValidator : IValidateOptions<SecurityOptions>
{
    public const string PlaceholderTokenSecret = "change-me-to-a-long-random-secret";
    public const int MinTokenSecretLength = 32;

    public ValidateOptionsResult Validate(string? name, SecurityOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.TokenSecret))
            failures.Add("Security:TokenSecret is required. Set it to a long, random, stable secret (see docker/.env.example).");
        else if (options.TokenSecret == PlaceholderTokenSecret)
            failures.Add("Security:TokenSecret is still the .env.example placeholder. Replace it with a long, random, stable secret.");
        else if (options.TokenSecret.Length < MinTokenSecretLength)
            failures.Add($"Security:TokenSecret must be at least {MinTokenSecretLength} characters (got {options.TokenSecret.Length}).");

        if (options.RequireMcpApiKey && !options.AllKeys.Any())
            failures.Add(
                "Security:AllowedApiKeys must contain at least one key when Security:RequireMcpApiKey is true. " +
                "Set Security__AllowedApiKeys__0, or Security__RequireMcpApiKey=false for a trusted private deployment.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

/// <summary>
/// Validates presented MCP server API keys against the configured allowlist, and whether a token's embedded
/// <c>apiKeyHash</c> is still allowlisted (the admin kill-switch: remove a key + restart → its tokens die).
/// All comparisons are constant-time. When
/// <see cref="SecurityOptions.RequireMcpApiKey"/> is <c>false</c>, every check passes — the API-key layer is off.
/// </summary>
public sealed class ApiKeyAllowlist(IOptions<SecurityOptions> options)
{
    private readonly SecurityOptions _options = options.Value;

    /// <summary>Whether an MCP server API key is required at all.</summary>
    public bool RequireApiKey => _options.RequireMcpApiKey;

    /// <summary>True when the server requires a key and has at least one configured.</summary>
    public bool IsEnabled => _options.RequireMcpApiKey && _options.AllKeys.Any();

    /// <summary>Constant-time check that a presented raw key is in the allowlist (always true when keys are off).</summary>
    public bool IsAllowed(string? presentedKey)
    {
        if (!_options.RequireMcpApiKey)
            return true;

        if (string.IsNullOrWhiteSpace(presentedKey))
            return false;

        var presented = Encoding.UTF8.GetBytes(presentedKey);
        var match = false;
        foreach (var key in _options.AllKeys)
            match |= CryptographicOperations.FixedTimeEquals(presented, Encoding.UTF8.GetBytes(key));
        return match;
    }

    /// <summary>True when a token's embedded key hash still corresponds to an allowlisted key (always true when keys are off).</summary>
    public bool IsHashAllowed(string? apiKeyHash)
    {
        if (!_options.RequireMcpApiKey)
            return true;

        if (string.IsNullOrWhiteSpace(apiKeyHash))
            return false;

        var presented = Encoding.UTF8.GetBytes(apiKeyHash);
        var match = false;
        foreach (var key in _options.AllKeys)
            match |= CryptographicOperations.FixedTimeEquals(
                presented, Encoding.UTF8.GetBytes(CredentialTokenService.HashApiKey(key)));
        return match;
    }
}
