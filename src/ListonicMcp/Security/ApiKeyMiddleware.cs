using System.Text;
using ListonicMcp.Security.OAuth;

namespace ListonicMcp.Security;

/// <summary>
/// Gates <c>/mcp</c> with either (a) a valid OAuth 2.1 Bearer access token issued by this server, or
/// (b) the header scheme: <c>X-Api-Key</c> (an allowlisted key, unless keys are turned off) +
/// <c>Authorization: Basic base64(email:password)</c>. Both resolve to the per-request Listonic credentials
/// stashed in <see cref="HttpContext.Items"/> for the credential provider. There is no server-side
/// default account.
/// </summary>
public class ApiKeyMiddleware(RequestDelegate next)
{
    public const string EmailItemKey    = "listonic-email";
    public const string PasswordItemKey = "listonic-password";

    public async Task InvokeAsync(
        HttpContext context,
        CredentialTokenService tokens,
        ApiKeyAllowlist allowlist,
        ILogger<ApiKeyMiddleware> logger)
    {
        var authHeader = context.Request.Headers.Authorization.FirstOrDefault();

        // Path A: OAuth Bearer access token.
        if (authHeader is not null && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var payload = tokens.TryUnprotect(authHeader["Bearer ".Length..].Trim(), TokenKind.Access);

            // Kill-switch: the embedded API-key hash must still be in the current allowlist.
            if (payload is null || !allowlist.IsHashAllowed(payload.ApiKeyHash))
            {
                logger.LogWarning("Rejected /mcp request: OAuth token invalid, expired, or its API key was revoked.");
                await Challenge(context);
                return;
            }

            context.Items[EmailItemKey]    = payload.Email;
            context.Items[PasswordItemKey] = payload.Password;
            await next(context);
            return;
        }

        // Path B: X-Api-Key + Basic. The key gate is enforced unless Security:RequireMcpApiKey is off.
        if (allowlist.RequireApiKey)
        {
            var provided = context.Request.Headers["X-Api-Key"].FirstOrDefault();
            if (!allowlist.IsAllowed(provided))
            {
                logger.LogWarning("Rejected /mcp request: missing or incorrect X-Api-Key.");
                // No key at all usually means an OAuth client that hasn't signed in yet: point it at the
                // metadata. A wrong key is a plain 401.
                if (string.IsNullOrEmpty(provided) && authHeader is null)
                {
                    await Challenge(context);
                    return;
                }
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("Unauthorized");
                return;
            }
        }

        var (email, password) = ParseBasicAuth(authHeader);
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            await Challenge(context,
                "Listonic credentials are required. Authorize via OAuth, or send an " +
                "Authorization: Basic header with your Listonic email and password.");
            return;
        }

        context.Items[EmailItemKey]    = email;
        context.Items[PasswordItemKey] = password;
        await next(context);
    }

    /// <summary>Emits 401 + a WWW-Authenticate pointing at the protected-resource metadata so clients (re-)auth.</summary>
    private static async Task Challenge(HttpContext context, string? message = null)
    {
        var resourceMetadata = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.PathBase}" +
                               "/.well-known/oauth-protected-resource";
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = $"Bearer resource_metadata=\"{resourceMetadata}\"";
        if (message is not null)
            await context.Response.WriteAsync(message);
    }

    private static (string? email, string? password) ParseBasicAuth(string? header)
    {
        if (header is null || !header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            return (null, null);

        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header["Basic ".Length..].Trim()));
            var sep = decoded.IndexOf(':');
            if (sep < 0) return (null, null);
            return (decoded[..sep], decoded[(sep + 1)..]);
        }
        catch (FormatException)
        {
            return (null, null);
        }
    }
}
