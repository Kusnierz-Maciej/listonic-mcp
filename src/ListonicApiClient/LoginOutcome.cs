namespace ListonicApiClient;

/// <summary>
/// Classifies a Listonic login attempt so callers can tell a genuine bad-credential rejection apart
/// from a transient API problem. Only an explicit credential rejection is <see cref="BadCredentials"/>;
/// rate limiting, 5xx and network faults are <see cref="ApiError"/>.
/// </summary>
public enum LoginOutcomeKind
{
    Ok,
    BadCredentials,
    ApiError,
}

/// <summary>The result of a login attempt.</summary>
/// <param name="Kind">The classification.</param>
/// <param name="Token">The bearer token when <see cref="LoginOutcomeKind.Ok"/>; otherwise null.</param>
/// <param name="Detail">Optional non-sensitive detail for logging (never contains credentials).</param>
public readonly record struct LoginOutcome(LoginOutcomeKind Kind, string? Token, string? Detail)
{
    public static LoginOutcome Success(string token) => new(LoginOutcomeKind.Ok, token, null);
    public static LoginOutcome Bad(string? detail = null) => new(LoginOutcomeKind.BadCredentials, null, detail);
    public static LoginOutcome Api(string? detail = null) => new(LoginOutcomeKind.ApiError, null, detail);
}

/// <summary>
/// Performs a Listonic login purely to validate credentials (used by the OAuth <c>/authorize</c> step).
/// Shares its classification with <see cref="ListonicAuthHandler"/> via <see cref="ListonicLogin"/>.
/// </summary>
public interface IListonicLoginValidator
{
    Task<LoginOutcome> ValidateAsync(string email, string password, CancellationToken ct = default);
}
