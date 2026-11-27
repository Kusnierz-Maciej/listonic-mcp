using System.Diagnostics;

namespace ListonicMcp;

// Pushes RequestId and TraceId into the log scope for every request so all
// logs from a single request can be correlated in Grafana/Loki regardless of
// whether OTLP tracing is enabled.
internal sealed class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var requestId = context.TraceIdentifier;
        var traceId = Activity.Current?.TraceId.ToString();

        using var scope = traceId is not null
            ? logger.BeginScope(new Dictionary<string, object>
            {
                ["RequestId"] = requestId,
                ["TraceId"] = traceId,
            })
            : logger.BeginScope(new Dictionary<string, object>
            {
                ["RequestId"] = requestId,
            });

        await next(context);
    }
}
