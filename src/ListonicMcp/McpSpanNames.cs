using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http.Features;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ListonicMcp;

/// <summary>
/// Names the HTTP request span of each <c>/mcp</c> request after the MCP message it carries — e.g.
/// <c>POST /mcp [tools/call get_items]</c> instead of <c>POST /mcp/</c> for every request — so trace lists are readable.
/// Source, in order of preference:
/// <list type="number">
/// <item>the <c>Mcp-Method</c> / <c>Mcp-Name</c> HTTP headers (2026-07-28 Streamable HTTP revision) — read in
/// <see cref="Middleware"/>, no MCP parsing involved;</item>
/// <item>fallback for clients that don't send them: the JSON-RPC message as the SDK parsed it, captured by
/// <see cref="MessageFilter"/>.</item>
/// </list>
/// The name is applied when the request finishes (in <see cref="Middleware"/>) and stored on the activity; with
/// OpenTelemetry on, the ASP.NET Core instrumentation resets the display name to <c>{method} {route}</c> after that, so
/// <see cref="Processor"/> re-applies it when the span ends, before export.
/// </summary>
public static class McpSpanNames
{
    public const string MethodHeader = "Mcp-Method";
    public const string NameHeader = "Mcp-Name";
    private const string ItemKey = "mcp.span_name";
    private const string PropertyKey = "mcp.span_name";

    /// <summary>
    /// <c>POST /mcp [tools/call get_items]</c>; without a target <c>POST /mcp [tools/list]</c>. The bracketed part equals
    /// the MCP SDK's own span name for the message, so one search matches both spans.
    /// </summary>
    public static string Format(HttpRequest request, string mcpMethod, string? target) =>
        string.IsNullOrEmpty(target)
            ? $"{request.Method} {request.Path} [{mcpMethod}]"
            : $"{request.Method} {request.Path} [{mcpMethod} {target}]";

    /// <summary>Middleware for the MCP endpoint: renames the request span when the request finishes.</summary>
    public static async Task Middleware(HttpContext context, RequestDelegate next)
    {
        try
        {
            await next(context);
        }
        finally
        {
            if (context.Features.Get<IHttpActivityFeature>()?.Activity is { } activity && NameFor(context) is { } name)
            {
                activity.DisplayName = name;
                activity.SetCustomProperty(PropertyKey, name);
            }
        }
    }

    /// <summary>
    /// Incoming message filter (fallback): remembers the MCP method and target of the message on its HTTP request.
    /// Only the first message of a request counts (one message per POST in Streamable HTTP).
    /// </summary>
    public static McpMessageFilter MessageFilter { get; } = next => async (context, ct) =>
    {
        var http = context.Services?.GetService<IHttpContextAccessor>()?.HttpContext;
        if (http is not null && !http.Items.ContainsKey(ItemKey))
        {
            var name = context.JsonRpcMessage switch
            {
                JsonRpcRequest r => Format(http.Request, r.Method, Target(r.Method, r.Params)),
                JsonRpcNotification n => Format(http.Request, n.Method, null),
                _ => null,
            };
            if (name is not null) http.Items[ItemKey] = name;
        }

        await next(context, ct);
    };

    private static string? NameFor(HttpContext context)
    {
        var headers = context.Request.Headers;
        if (headers[MethodHeader] is [{ Length: > 0 } method, ..])
            return Format(context.Request, method, headers[NameHeader] is [{ Length: > 0 } target, ..] ? target : null);
        return context.Items[ItemKey] as string;
    }

    /// <summary>What <c>Mcp-Name</c> would carry: the tool/prompt name or the resource URI.</summary>
    private static string? Target(string method, JsonNode? @params) => method switch
    {
        RequestMethods.ToolsCall or RequestMethods.PromptsGet => (@params?["name"] as JsonValue)?.GetValue<string>(),
        RequestMethods.ResourcesRead or RequestMethods.ResourcesSubscribe or RequestMethods.ResourcesUnsubscribe
            => (@params?["uri"] as JsonValue)?.GetValue<string>(),
        _ => null,
    };

    /// <summary>
    /// OpenTelemetry processor: re-applies the MCP name when the span ends (after the ASP.NET Core instrumentation has
    /// set <c>{method} {route}</c>). Add it before the exporter.
    /// </summary>
    public sealed class Processor : OpenTelemetry.BaseProcessor<Activity>
    {
        public override void OnEnd(Activity activity)
        {
            if (activity.GetCustomProperty(PropertyKey) is string name)
                activity.DisplayName = name;
        }
    }
}
