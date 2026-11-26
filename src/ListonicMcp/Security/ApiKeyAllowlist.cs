using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace ListonicMcp.Security;

/// <summary>Binds the <c>Security</c> section.</summary>
public sealed class SecurityOptions
{
    public const string Section = "Security";

    /// <summary>MCP server API keys allowed to use the server. Add one per user/client.</summary>
    public string[] AllowedApiKeys { get; set; } = [];

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

/// <summary>Startup rules for <see cref="SecurityOptions"/> (web host only — stdio has no API key).</summary>
public sealed class SecurityOptionsValidator : IValidateOptions<SecurityOptions>
{
    public ValidateOptionsResult Validate(string? name, SecurityOptions options)
    {
        var failures = new List<string>();

        if (options.RequireMcpApiKey && !options.AllKeys.Any())
            failures.Add(
                "Security:AllowedApiKeys must contain at least one key when Security:RequireMcpApiKey is true. " +
                "Set Security__AllowedApiKeys__0, or Security__RequireMcpApiKey=false for a trusted private deployment.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

/// <summary>
/// Validates presented MCP server API keys against the configured allowlist, in constant time. When
/// <see cref="SecurityOptions.RequireMcpApiKey"/> is <c>false</c>, every check passes — the API-key layer is off.
/// </summary>
public sealed class ApiKeyAllowlist(IOptions<SecurityOptions> options)
{
    private readonly SecurityOptions _options = options.Value;

    /// <summary>Whether an MCP server API key is required at all.</summary>
    public bool RequireApiKey => _options.RequireMcpApiKey;

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
}
