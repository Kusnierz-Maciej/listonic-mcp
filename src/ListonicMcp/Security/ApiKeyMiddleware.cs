using System.Text;

namespace ListonicMcp.Security;

/// <summary>
/// Gates <c>/mcp</c> with <c>X-Api-Key</c> (an allowlisted MCP server key, unless keys are turned off) plus
/// <c>Authorization: Basic base64(email:password)</c> carrying the caller's own Listonic credentials,
/// which are stashed in <see cref="HttpContext.Items"/> for the credential provider. There is no
/// server-side default account.
/// </summary>
public class ApiKeyMiddleware(RequestDelegate next)
{
    public const string EmailItemKey    = "listonic-email";
    public const string PasswordItemKey = "listonic-password";

    public async Task InvokeAsync(HttpContext context, ApiKeyAllowlist allowlist, ILogger<ApiKeyMiddleware> logger)
    {
        // The key gate is enforced unless Security:RequireMcpApiKey is off.
        var provided = context.Request.Headers["X-Api-Key"].FirstOrDefault();
        if (allowlist.RequireApiKey && !allowlist.IsAllowed(provided))
        {
            logger.LogWarning("Rejected /mcp request: missing or incorrect X-Api-Key.");
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Unauthorized");
            return;
        }

        var (email, password) = ParseBasicAuth(context.Request.Headers.Authorization.FirstOrDefault());
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync(
                "Listonic credentials are required. Send an Authorization: Basic header with your " +
                "Listonic email and password.");
            return;
        }

        context.Items[EmailItemKey]    = email;
        context.Items[PasswordItemKey] = password;

        await next(context);
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
