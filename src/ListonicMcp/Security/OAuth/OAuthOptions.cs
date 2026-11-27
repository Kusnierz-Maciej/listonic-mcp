using Microsoft.Extensions.Options;

namespace ListonicMcp.Security.OAuth;

/// <summary>Binds the <c>OAuth</c> section: lifetimes of the tokens the built-in authorization server issues.</summary>
public sealed class OAuthOptions
{
    public const string Section = "OAuth";

    /// <summary>Single-use authorization code (sign-in → token exchange). Default 1 minute.</summary>
    public TimeSpan AuthorizationCodeLifetime { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Access token sent on every /mcp call. Default 1 hour.</summary>
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Refresh token (rotated on every refresh): how long a client may stay idle before signing in again. Default 365 days.</summary>
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(365);
}

/// <summary>All lifetimes positive; code ≤ 10 min (it only bridges a redirect); access ≤ refresh.</summary>
public sealed class OAuthOptionsValidator : IValidateOptions<OAuthOptions>
{
    public ValidateOptionsResult Validate(string? name, OAuthOptions o)
    {
        var failures = new List<string>();
        if (o.AuthorizationCodeLifetime <= TimeSpan.Zero || o.AuthorizationCodeLifetime > TimeSpan.FromMinutes(10))
            failures.Add($"OAuth:AuthorizationCodeLifetime must be greater than 0 and at most 00:10:00 (got {o.AuthorizationCodeLifetime}).");
        if (o.AccessTokenLifetime <= TimeSpan.Zero)
            failures.Add($"OAuth:AccessTokenLifetime must be greater than 0 (got {o.AccessTokenLifetime}).");
        if (o.RefreshTokenLifetime <= TimeSpan.Zero)
            failures.Add($"OAuth:RefreshTokenLifetime must be greater than 0 (got {o.RefreshTokenLifetime}).");
        else if (o.AccessTokenLifetime > o.RefreshTokenLifetime)
            failures.Add("OAuth:AccessTokenLifetime must not be longer than OAuth:RefreshTokenLifetime.");
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
