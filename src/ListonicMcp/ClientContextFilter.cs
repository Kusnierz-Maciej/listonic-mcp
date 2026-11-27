using System.Text.Json.Nodes;
using ListonicApiClient;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ListonicMcp;

/// <summary>
/// Records, for every tool call, which client made it and whether that client propagated W3C trace context —
/// via <c>params._meta</c> (SEP-414: <c>traceparent</c>/<c>tracestate</c>/<c>baggage</c>) or the HTTP <c>traceparent</c>
/// header. When it does, the SDK (and ASP.NET Core) already continue the client's trace, so all tool calls of one chat
/// turn land in a single trace; this filter makes it visible which clients actually do that.
/// Only key names are recorded, never <c>_meta</c> values.
/// </summary>
internal static class ClientContextFilter
{
    private const string ClientInfoKey = "io.modelcontextprotocol/clientInfo";
    private static readonly string[] TraceKeys = ["traceparent", "tracestate", "baggage"];

    public static McpRequestHandler<CallToolRequestParams, CallToolResult> Create(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next) =>
        async (context, ct) =>
        {
            var info = Inspect(context);

            ListonicTelemetry.Tag(ListonicTelemetry.Tags.ClientName, info.ClientName);
            ListonicTelemetry.Tag(ListonicTelemetry.Tags.ClientVersion, info.ClientVersion);
            ListonicTelemetry.Tag(ListonicTelemetry.Tags.ClientTraceContext, info.TraceContext);
            ListonicTelemetry.Tag(ListonicTelemetry.Tags.ClientMetaKeys, info.MetaKeys);

            context.Services?.GetService<ILoggerFactory>()?.CreateLogger(typeof(ClientContextFilter))
                .LogInformation("tools/call {Tool} from {ClientName} {ClientVersion}: trace context {TraceContext}; _meta keys [{MetaKeys}]",
                    context.Params?.Name, info.ClientName ?? "unknown", info.ClientVersion ?? "", info.TraceContext,
                    string.Join(", ", info.MetaKeys));

            return await next(context, ct);
        };

    internal sealed record ClientInfo(string? ClientName, string? ClientVersion, string TraceContext, string[] MetaKeys);

    internal static ClientInfo Inspect(RequestContext<CallToolRequestParams> context)
    {
        var meta = context.Params?.Meta;
        var metaKeys = meta?.Select(p => p.Key).Order(StringComparer.Ordinal).ToArray() ?? [];

        // Client identity: per-request _meta (2026-07-28 stateless protocol), else the initialize handshake.
        string? name = null, version = null;
        if (meta?[ClientInfoKey] is JsonObject clientInfo)
        {
            name = clientInfo["name"]?.GetValue<string>();
            version = clientInfo["version"]?.GetValue<string>();
        }
        name ??= context.Server?.ClientInfo?.Name;
        version ??= context.Server?.ClientInfo?.Version;

        var http = context.Services?.GetService<IHttpContextAccessor>()?.HttpContext;
        var traceContext =
            meta is not null && TraceKeys.Any(k => meta.ContainsKey(k)) ? "meta"
            : http?.Request.Headers.ContainsKey("traceparent") == true ? "http"
            : "none";

        return new ClientInfo(name, version, traceContext, metaKeys);
    }
}
