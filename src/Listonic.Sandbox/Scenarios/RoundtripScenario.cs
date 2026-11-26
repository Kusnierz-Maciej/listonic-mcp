using Listonic.Sandbox.Dumping;
using ListonicApiClient;
using ListonicApiClient.Models.Items;
using ListonicApiClient.Models.Lists;
using Microsoft.Extensions.DependencyInjection;
using Refit;

namespace Listonic.Sandbox.Scenarios;

/// <summary>
/// WHAT: WRITES, but only to its own list. Creates "[test] sandbox …", adds items, checks/edits one,
/// deletes one, probes error responses (unknown list / item ids) and payload shapes, then deletes the list.
///
/// WHY: one run captures every write endpoint's request and response shape, plus the error statuses
/// and bodies the tool-layer error mapping is built on.
///
/// FOUND (2026-11-27): unknown list → GET /lists/{id} 404 (RFC 7807 body) but GET /lists/{id}/items
/// 200 []; rename 200 (silent), delete 404; unknown item → PATCH 404 but DELETE 200 (silent). Add returns
/// 201 with the full item; no amount = "", cleared = "0". Deleted list → 404, not in archive listing.
/// USE IT FOR: payload formats and error shapes after API changes. Never touches other lists.
/// </summary>
public sealed class RoundtripScenario : IScenario
{
    public string Name => "roundtrip";
    public string Description => "Writes to its own [test] list only: create/add/edit/delete + error probes.";

    public async Task RunAsync(IServiceProvider services, DumpRun run, string[] args, CancellationToken ct)
    {
        var api = services.GetRequiredService<IListonicApi>();

        var list = await api.CreateListAsync(
            new CreateListRequest { Name = $"[test] sandbox {DateTime.UtcNow:yyyyMMdd-HHmmss}" }, ct);
        run.Log($"created [{list.Id}] '{list.Name}'");

        try
        {
            var milk = await api.AddItemAsync(list.Id, new AddItemRequest { Name = "mleko", Quantity = 2, Unit = "l" }, ct);
            var bread = await api.AddItemAsync(list.Id, new AddItemRequest { Name = "chleb" }, ct);
            run.Log($"added milk=[{milk.Id}] bread=[{bread.Id}]");

            await api.UpdateItemAsync(list.Id, milk.Id, new UpdateItemRequest { IsChecked = true }, ct);
            await api.UpdateItemAsync(list.Id, bread.Id, new UpdateItemRequest { Amount = 0.5m, Unit = "szt." }, ct);
            await api.GetItemsAsync(list.Id, ct);

            await api.DeleteItemAsync(list.Id, bread.Id, ct);
            await api.RenameListAsync(list.Id, new RenameListRequest { Id = list.Id, Name = list.Name + " (renamed)" }, ct);

            // Error probes — record status + body for the tool-layer error mapping.
            await ProbeAsync(run, "get unknown list", () => api.GetListAsync("999999999", ct));
            await ProbeAsync(run, "items of unknown list", () => api.GetItemsAsync("999999999", ct));
            await ProbeAsync(run, "update unknown item", () =>
                api.UpdateItemAsync(list.Id, "999999999", new UpdateItemRequest { IsChecked = true }, ct));
            await ProbeAsync(run, "delete unknown item", () => api.DeleteItemAsync(list.Id, "999999999", ct));
            await ProbeAsync(run, "rename unknown list", () =>
                api.RenameListAsync("999999999", new RenameListRequest { Id = "999999999", Name = "x" }, ct));
            await ProbeAsync(run, "delete unknown list", () => api.DeleteListAsync("999999999", ct));

            // Contract probes: shapes the tools format from.
            var created = (await api.GetListsAsync(new GetListsRequest { IncludeShares = true }, ct)).Single(l => l.Id == list.Id);
            run.Log($"new list: shares={created.Shares.Count} isOwner={created.IsOwner}");
            var plain = await api.AddItemAsync(list.Id, new AddItemRequest { Name = "żółć ąęśń" }, ct);
            run.Log($"no-amount item: amount='{plain.Amount}' unit='{plain.Unit}' name='{plain.Name}'");
            await api.UpdateItemAsync(list.Id, milk.Id, new UpdateItemRequest { Amount = 0, Unit = "" }, ct);
            var cleared = (await api.GetItemsAsync(list.Id, ct)).Single(i => i.Id == milk.Id);
            run.Log($"cleared quantity: amount='{cleared.Amount}' unit='{cleared.Unit}'");
        }
        finally
        {
            await api.DeleteListAsync(list.Id, ct);
            run.Log($"deleted [{list.Id}]");
            await ProbeAsync(run, "get deleted list", async () =>
            {
                var gone = await api.GetListAsync(list.Id, ct);
                run.Log($"  deleted list still readable: deleted={gone.Deleted} active={gone.Active}");
            });
            var archived = await api.GetListsAsync(new GetListsRequest { IncludeShares = true, Archive = true }, ct);
            run.Log($"deleted list in archive=true listing: {archived.Any(l => l.Id == list.Id)}");
        }
    }

    private static async Task ProbeAsync(DumpRun run, string label, Func<Task> call)
    {
        try
        {
            await call();
            run.Log($"{label}: succeeded (no error)");
        }
        catch (ApiException ex)
        {
            run.Log($"{label}: HTTP {(int)ex.StatusCode} {ex.StatusCode} body={ex.Content}");
        }
    }
}
