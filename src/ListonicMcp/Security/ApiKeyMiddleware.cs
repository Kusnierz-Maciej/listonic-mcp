namespace ListonicMcp.Security;

public class ApiKeyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string? _apiKey;

    public ApiKeyMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        _apiKey = configuration["Security:ApiKey"];
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            await _next(context);
            return;
        }

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

        await _next(context);
    }
}
