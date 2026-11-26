using Listonic.Sandbox.Dumping;
using ListonicApiClient;
using ListonicMcp.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace Listonic.Sandbox.Scenarios;

/// <summary>
/// WHAT: READ-ONLY. Runs the MCP tool layer (<see cref="ListonicTools"/>) rather than the raw API:
/// get_lists, then get_items for the given list (or the first list), printing the exact tool output.
///
/// WHY: separates "the API returned something odd" from "the tool formatted it oddly" — the dumps
/// show the wire, run.log shows what the model would see.
///
/// FOUND / USE IT FOR: checking tool text output after formatting changes.
/// </summary>
public sealed class ToolsSmokeScenario : IScenario
{
    public string Name => "tools-smoke";
    public string Description => "Read-only: get_lists + get_items through the MCP tool layer.";

    public async Task RunAsync(IServiceProvider services, DumpRun run, string[] args, CancellationToken ct)
    {
        var api = services.GetRequiredService<IListonicApi>();
        var tools = ActivatorUtilities.CreateInstance<ListonicTools>(services);

        run.Log("get_lists →\n" + await tools.GetListsAsync(ct));

        var list = args.Length > 0
            ? await ListResolver.ResolveAsync(api, string.Join(' ', args), ct)
            : (await api.GetListsAsync(new() { IncludeShares = true }, ct)).FirstOrDefault();
        if (list is null)
        {
            run.Log("no lists — skipping get_items.");
            return;
        }

        run.Log($"get_items [{list.Id}] →\n" + await tools.GetItemsAsync(list.Id, list.Name, ct));
    }
}
