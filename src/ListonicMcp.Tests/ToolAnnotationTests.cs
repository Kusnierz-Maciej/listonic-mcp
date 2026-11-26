using System.Reflection;
using ListonicMcp.Tools;
using ModelContextProtocol.Server;

namespace ListonicMcp.Tests;

public class ToolAnnotationTests
{
    private static McpServerToolAttribute Tool(string name) =>
        typeof(ListonicTools).GetMethods()
            .Select(m => m.GetCustomAttribute<McpServerToolAttribute>())
            .Single(a => a?.Name == name)!;

    [Test]
    [Arguments("get_lists")]
    [Arguments("get_items")]
    public async Task Reads_AreReadOnly(string name) =>
        await Assert.That(Tool(name).ReadOnly).IsTrue();

    [Test]
    [Arguments("delete_items")]
    [Arguments("delete_list")]
    public async Task Deletes_AreDestructive(string name) =>
        await Assert.That(Tool(name).Destructive).IsTrue();

    [Test]
    [Arguments("add_items")]
    [Arguments("edit_items")]
    [Arguments("rename_list")]
    public async Task Writes_AreNotDestructive(string name)
    {
        await Assert.That(Tool(name).ReadOnly).IsFalse();
        await Assert.That(Tool(name).Destructive).IsFalse();
    }

    [Test]
    public async Task EveryTool_HasATitle() =>
        await Assert.That(typeof(ListonicTools).GetMethods()
            .Select(m => m.GetCustomAttribute<McpServerToolAttribute>())
            .Where(a => a is not null)
            .All(a => !string.IsNullOrWhiteSpace(a!.Title))).IsTrue();
}
