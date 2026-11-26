using Listonic.Sandbox.Dumping;
using ListonicApiClient;
using ListonicApiClient.Models.Lists;
using Microsoft.Extensions.DependencyInjection;

namespace Listonic.Sandbox.Scenarios;

/// <summary>
/// WHAT: READ-ONLY. Fetches account userinfo, then all lists in the variants the server uses
/// (with shares, with archive, with items), dumping every request/response in full.
///
/// WHY: the first, always-safe scenario — proves the dump pipeline end-to-end (login included) and
/// shows the raw list payloads (counts, shares, owner flags) the get_lists tool is built on.
///
/// FOUND / USE IT FOR: the login exchange (token TTL), the exact list JSON shape, how shared lists
/// look, and as the template to copy when writing a new read-only probe.
/// </summary>
public sealed class DumpListsScenario : IScenario
{
    public string Name => "dump-lists";
    public string Description => "Read-only: userinfo + lists (shares / archive / items variants).";

    public async Task RunAsync(IServiceProvider services, DumpRun run, string[] args, CancellationToken ct)
    {
        var api = services.GetRequiredService<IListonicApi>();

        await api.GetUserInfoAsync(ct);

        var lists = await api.GetListsAsync(new GetListsRequest { IncludeShares = true }, ct);
        run.Log($"lists (includeShares): {lists.Count}");
        foreach (var l in lists)
            run.Log($"  [{l.Id}] '{l.Name}' owner={l.IsOwner} shares={l.Shares.Count} " +
                    $"items={l.ItemsCount} checked={l.CheckedItemsCount}");

        var archived = await api.GetListsAsync(new GetListsRequest { IncludeShares = true, Archive = true }, ct);
        run.Log($"lists (archive=true): {archived.Count}");

        var withItems = await api.GetListsAsync(new GetListsRequest { IncludeShares = true, IncludeItems = true }, ct);
        run.Log($"lists (includeItems=true): {withItems.Sum(l => l.Items.Count)} items total");
    }
}
