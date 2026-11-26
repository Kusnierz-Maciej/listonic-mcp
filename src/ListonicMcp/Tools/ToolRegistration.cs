using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;

namespace ListonicMcp.Tools;

/// <summary>
/// Registers the <see cref="ListonicTools"/> methods as MCP tools (same as <c>WithTools&lt;ListonicTools&gt;()</c>:
/// a fresh instance per call from the request's services) but with portable input schemas.
/// </summary>
internal static class ToolRegistration
{
    public static IMcpServerBuilder WithListonicTools(this IMcpServerBuilder builder) =>
        builder.WithTools(CreateTools());

    internal static IEnumerable<McpServerTool> CreateTools() =>
        typeof(ListonicTools)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null)
            .Select(m => McpServerTool.Create(
                m,
                ctx => ActivatorUtilities.CreateInstance<ListonicTools>(ctx.Services!),
                new McpServerToolCreateOptions
                {
                    SchemaCreateOptions = new AIJsonSchemaCreateOptions { TransformSchemaNode = PortableNullable },
                }))
            .ToList();

    /// <summary>
    /// Rewrites a nullable type array — <c>"type": ["number", "null"]</c>, legal JSON Schema but refused or
    /// mishandled by clients that map schemas onto a single-<c>type</c> dialect (e.g. Gemini's OpenAPI subset) —
    /// into <c>"anyOf": [{"type": "number"}, {"type": "null"}]</c>. Other keywords (description, default…)
    /// stay on the outer node.
    /// </summary>
    internal static JsonNode PortableNullable(AIJsonSchemaCreateContext context, JsonNode schema)
    {
        if (schema is not JsonObject obj || obj["type"] is not JsonArray types || types.Count < 2)
            return schema;

        obj.Remove("type");
        var branches = new JsonArray();
        foreach (var type in types)
            branches.Add(new JsonObject { ["type"] = type?.DeepClone() });
        obj["anyOf"] = branches;
        return obj;
    }
}
