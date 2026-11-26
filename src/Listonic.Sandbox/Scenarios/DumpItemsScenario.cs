using Listonic.Sandbox.Dumping;
using ListonicApiClient;
using Microsoft.Extensions.DependencyInjection;

namespace Listonic.Sandbox.Scenarios;

/// <summary>
/// WHAT: READ-ONLY. <c>dump-items &lt;listId|name&gt;</c> — the single list and its raw items.
///
/// WHY: get_items / edit_items behaviour depends on the raw item fields (Amount is a string,
/// Checked is 0/1, units are free text). This shows them exactly as the API sends them.
///
/// FOUND / USE IT FOR: odd amounts/units, duplicate-name rows, checked/unchecked ordering issues.
/// </summary>
public sealed class DumpItemsScenario : IScenario
{
    public string Name => "dump-items";
    public string Description => "Read-only: one list (by id or name) and its raw items.";

    public async Task RunAsync(IServiceProvider services, DumpRun run, string[] args, CancellationToken ct)
    {
        if (args.Length == 0)
            throw new ArgumentException("Usage: dump-items <listId|list name>");

        var api = services.GetRequiredService<IListonicApi>();
        var list = await ListResolver.ResolveAsync(api, string.Join(' ', args), ct);

        await api.GetListAsync(list.Id, ct);
        var items = await api.GetItemsAsync(list.Id, ct);
        run.Log($"[{list.Id}] '{list.Name}': {items.Count} item(s)");
        foreach (var i in items)
            run.Log($"  {(i.Checked ? "[x]" : "[ ]")} [{i.Id}] '{i.Name}' amount='{i.Amount}' unit='{i.Unit}' " +
                    $"category={i.CategoryId}");
    }
}
