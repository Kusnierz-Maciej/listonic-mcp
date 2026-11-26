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

        if (!options.AllKeys.Any())
            failures.Add("Security:AllowedApiKeys must contain at least one key. Set Security__AllowedApiKeys__0.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

/// <summary>Validates presented MCP server API keys against the configured allowlist, in constant time.</summary>
public sealed class ApiKeyAllowlist(IOptions<SecurityOptions> options)
{
    private readonly SecurityOptions _options = options.Value;

    /// <summary>Constant-time check that a presented raw key is in the allowlist.</summary>
    public bool IsAllowed(string? presentedKey)
    {
        if (string.IsNullOrWhiteSpace(presentedKey))
            return false;

        var presented = Encoding.UTF8.GetBytes(presentedKey);
        var match = false;
        foreach (var key in _options.AllKeys)
            match |= CryptographicOperations.FixedTimeEquals(presented, Encoding.UTF8.GetBytes(key));
        return match;
    }
}
