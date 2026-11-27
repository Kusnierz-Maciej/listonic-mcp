namespace ListonicApiClient;

/// <summary>
/// Thrown when Listonic rejects the stored credentials — either the login itself is refused, or an API
/// call still returns 401 right after a fresh re-login (typically the user changed their password after
/// authorizing). The MCP layer maps this to HTTP 401 + <c>WWW-Authenticate</c> so the client re-runs
/// OAuth. A transient API error is NOT represented by this exception.
/// </summary>
public sealed class ListonicCredentialsRejectedException(string? detail = null)
    : Exception("Listonic rejected the stored credentials; re-authentication is required." +
                (detail is null ? "" : $" ({detail})"));
