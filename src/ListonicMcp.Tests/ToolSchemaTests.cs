using System.Text.Json;
using ListonicMcp.Tools;

namespace ListonicMcp.Tests;

/// <summary>
/// Tool input schemas must be portable: no <c>"type": [..]</c> arrays anywhere (some MCP clients map schemas
/// onto a single-type dialect and reject or drop them). Registration must keep names and annotations.
/// </summary>
public class ToolSchemaTests
{
    private static readonly List<ModelContextProtocol.Server.McpServerTool> Tools = ToolRegistration.CreateTools().ToList();

    [Test]
    public async Task AllEightTools_Registered_WithAnnotations()
    {
        await Assert.That(Tools.Count).IsEqualTo(8);
        await Assert.That(Tools.All(t => t.ProtocolTool.Annotations?.Title is { Length: > 0 })).IsTrue();
        var getLists = Tools.Single(t => t.ProtocolTool.Name == "get_lists").ProtocolTool;
        await Assert.That(getLists.Annotations!.ReadOnlyHint).IsTrue();
    }

    [Test]
    public async Task NoSchema_UsesTypeArrays()
    {
        var offenders = Tools.SelectMany(t => TypeArrays(t.ProtocolTool.InputSchema, t.ProtocolTool.Name)).ToList();
        await Assert.That(offenders).IsEmpty();
    }

    [Test]
    public async Task NullableFields_BecomeAnyOf_KeepingDescription()
    {
        var edit = Tools.Single(t => t.ProtocolTool.Name == "edit_items").ProtocolTool.InputSchema;
        var isChecked = edit.GetProperty("properties").GetProperty("items").GetProperty("items")
            .GetProperty("properties").GetProperty("isChecked");

        await Assert.That(isChecked.GetProperty("anyOf").GetArrayLength()).IsEqualTo(2);
        await Assert.That(isChecked.GetProperty("description").GetString()!).Contains("bought");
    }

    private static IEnumerable<string> TypeArrays(JsonElement node, string path)
    {
        switch (node.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in node.EnumerateObject())
                {
                    if (p.Name == "type" && p.Value.ValueKind == JsonValueKind.Array)
                        yield return path;
                    foreach (var x in TypeArrays(p.Value, $"{path}.{p.Name}")) yield return x;
                }
                break;
            case JsonValueKind.Array:
                var i = 0;
                foreach (var e in node.EnumerateArray())
                    foreach (var x in TypeArrays(e, $"{path}[{i++}]")) yield return x;
                break;
        }
    }
}
