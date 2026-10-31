namespace ListonicMcp.Security;

public class ApiKeyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string? _apiKey;
    private readonly string? _fallbackEmail;
    private readonly string? _fallbackPassword;

    public ApiKeyMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        _apiKey        = configuration["Security:ApiKey"];
        _fallbackEmail    = configuration["Listonic:Email"];
        _fallbackPassword = configuration["Listonic:Password"];
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            string? provided = null;

            var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
            if (authHeader is not null && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                provided = authHeader["Bearer ".Length..].Trim();

            if (provided is null)
                provided = context.Request.Headers["X-Api-Key"].FirstOrDefault();

            if (provided != _apiKey)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("Unauthorized");
                return;
            }
        }

        var email    = context.Request.Headers["X-Listonic-Email"].FirstOrDefault()    ?? _fallbackEmail;
        var password = context.Request.Headers["X-Listonic-Password"].FirstOrDefault() ?? _fallbackPassword;

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync(
                "Listonic credentials are required. Provide X-Listonic-Email and X-Listonic-Password headers, " +
                "or configure Listonic:Email and Listonic:Password on the server.");
            return;
        }

        context.Items["listonic-email"]    = email;
        context.Items["listonic-password"] = password;

        await _next(context);
    }
}
