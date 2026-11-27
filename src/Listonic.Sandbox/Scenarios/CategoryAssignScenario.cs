using System.Text.Json;
using Listonic.Sandbox.Dumping;
using ListonicApiClient;
using ListonicApiClient.Models.Items;
using ListonicApiClient.Models.Lists;
using Microsoft.Extensions.DependencyInjection;

namespace Listonic.Sandbox.Scenarios;

/// <summary>
/// WHAT: WRITES, but only to its own list. Creates "[test] categories …", adds a mix of product names
/// (common Polish groceries, English, a typo, a non-food item, nonsense), and maps each item's
/// server-assigned CategoryId onto /categoriesv2. Deletes the list at the end.
///
/// WHY: groundwork for a categories feature (S9b) — does Listonic always assign a category, is it
/// stable, and what happens to names it doesn't recognise?
///
/// FOUND (2026-11-27): CategoryId is always assigned from the name (Polish and English alike); unknown
/// names and typos get 1 = "Other"; every id is in /categoriesv2; re-reads are stable.
/// </summary>
public sealed class CategoryAssignScenario : IScenario
{
    private static readonly string[] Names =
    [
        "mleko", "chleb", "banany", "masło", "papier toaletowy", "szampon",
        "milk", "bread", "mlekko", "xyzqwerty", "kombucha", "mleko",
    ];

    public string Name => "category-assign";
    public string Description => "Writes to its own [test] list only: which CategoryId Listonic assigns to new items.";

    public async Task RunAsync(IServiceProvider services, DumpRun run, string[] args, CancellationToken ct)
    {
        var api = services.GetRequiredService<IListonicApi>();

        var categories = ((JsonElement)await api.GetCategoriesAsync(ct)).EnumerateArray()
            .ToDictionary(c => c.GetProperty("Id").ToString(), c => c.GetProperty("Name").GetString());
        run.Log($"categoriesv2: {categories.Count} — ids {string.Join(", ", categories.Keys.OrderBy(k => k.Length).ThenBy(k => k))}");

        var list = await api.CreateListAsync(
            new CreateListRequest { Name = $"[test] categories {DateTime.UtcNow:yyyyMMdd-HHmmss}" }, ct);
        try
        {
            foreach (var name in Names)
            {
                var added = await api.AddItemAsync(list.Id, new AddItemRequest { Name = name }, ct);
                var id = added.CategoryId.ToString();
                run.Log($"add '{name}': CategoryId={id} → {(categories.TryGetValue(id, out var n) ? $"'{n}'" : "NOT IN categoriesv2")}");
            }

            var reread = await api.GetItemsAsync(list.Id, ct);
            run.Log("re-read: " + string.Join(", ", reread.Select(i => $"{i.Name}={i.CategoryId}")));
        }
        finally
        {
            await api.DeleteListAsync(list.Id, ct);
            run.Log($"deleted [{list.Id}]");
        }
    }
}
