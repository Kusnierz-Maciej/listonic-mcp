using ListonicApiClient;
using ListonicApiClient.Models.Lists;

namespace Listonic.Sandbox.Scenarios;

internal static class ListResolver
{
    /// <summary>Finds a list by exact id, else by case-insensitive name. Throws with the known names when absent.</summary>
    public static async Task<ShoppingList> ResolveAsync(IListonicApi api, string idOrName, CancellationToken ct)
    {
        var lists = await api.GetListsAsync(new GetListsRequest { IncludeShares = true }, ct);
        return lists.FirstOrDefault(l => l.Id == idOrName)
            ?? lists.FirstOrDefault(l => string.Equals(l.Name, idOrName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"No list '{idOrName}'. Known: {string.Join(", ", lists.Select(l => $"[{l.Id}] {l.Name}"))}");
    }
}
