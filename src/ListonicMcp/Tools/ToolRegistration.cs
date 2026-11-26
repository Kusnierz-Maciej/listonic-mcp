using System.Buffers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using ModelContextProtocol;
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

    /// <summary>
    /// Argument binding: the SDK defaults plus <see cref="LenientStringConverter"/>, so ids sent as JSON numbers
    /// (Listonic ids look numeric, and some clients/models send them unquoted) bind to string parameters
    /// instead of failing before the tool runs with an opaque error.
    /// </summary>
    private static readonly JsonSerializerOptions ArgumentOptions = CreateArgumentOptions();

    private static JsonSerializerOptions CreateArgumentOptions()
    {
        var options = new JsonSerializerOptions(McpJsonUtilities.DefaultOptions);
        options.Converters.Add(new LenientStringConverter());
        options.MakeReadOnly();
        return options;
    }

    internal static IEnumerable<McpServerTool> CreateTools() =>
        typeof(ListonicTools)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null)
            .Select(CreateTool)
            .ToList();

    private static McpServerTool CreateTool(MethodInfo method)
    {
        static ListonicTools Target(RequestContext<ModelContextProtocol.Protocol.CallToolRequestParams> ctx) =>
            ActivatorUtilities.CreateInstance<ListonicTools>(ctx.Services!);

        // The advertised schema comes from the standard serializer options (accurate types) + the portability
        // transform; arguments are bound with the lenient options. A custom string converter would hide the
        // "string" type from the schema exporter, so the two are built separately and the schema copied over.
        var schemaSource = McpServerTool.Create(method, Target, new McpServerToolCreateOptions
        {
            SchemaCreateOptions = new AIJsonSchemaCreateOptions { TransformSchemaNode = PortableNullable },
        });
        var tool = McpServerTool.Create(method, Target, new McpServerToolCreateOptions
        {
            SerializerOptions = ArgumentOptions,
        });
        tool.ProtocolTool.InputSchema = schemaSource.ProtocolTool.InputSchema;
        return tool;
    }

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

    /// <summary>Reads JSON strings normally and JSON numbers as their literal text; writes plain strings.</summary>
    internal sealed class LenientStringConverter : JsonConverter<string>
    {
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType switch
            {
                JsonTokenType.String => reader.GetString(),
                JsonTokenType.Number => Encoding.UTF8.GetString(reader.HasValueSequence
                    ? reader.ValueSequence.ToArray() : reader.ValueSpan.ToArray()),
                JsonTokenType.Null => null,
                _ => throw new JsonException($"Expected a string, got {reader.TokenType}."),
            };

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value);
    }
}
