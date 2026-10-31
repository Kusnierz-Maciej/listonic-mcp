using System.ComponentModel;
using ListonicApiClient;
using ListonicApiClient.Models.Items;
using ListonicApiClient.Models.Lists;
using ModelContextProtocol.Server;

namespace ListonicMcp.Tools;

[McpServerToolType]
public sealed class ListonicTools(IListonicApi api)
{
    public record AddItemInput(
        [property: Description("Item name")] string Name,
        [property: Description("Quantity, e.g. '2', '500g'")] string? Amount = null,
        [property: Description("Unit, e.g. 'kg', 'pcs', 'liters'")] string? Unit = null);

    public record EditItemInput(
        [property: Description("Item ID (from get_items)")] string ItemId,
        [property: Description("true = check off (user has it), false = uncheck")] bool? IsChecked = null,
        [property: Description("New item name")] string? Name = null,
        [property: Description("New quantity string")] string? Quantity = null);

    [McpServerTool(Name = "get_lists")]
    [Description("All shopping lists on the account, with their IDs. Use when targeting a specific list.")]
    public async Task<string> GetListsAsync(CancellationToken ct = default)
    {
        var lists = await api.GetListsAsync(new GetListsRequest
        {
            IncludeShares = false,
            Archive = false,
            IncludeItems = false,
        }, ct);

        if (lists.Count == 0)
            return "No lists found.";

        return string.Join("\n", lists.Select(l => $"[{l.Id}] {l.Name}"));
    }

    [McpServerTool(Name = "get_items")]
    [Description("Fetch all items on a list. Checked ([x]) = user already has it; unchecked ([ ]) = still to buy. Call before editing or deleting to get item IDs.")]
    public async Task<string> GetItemsAsync(
        [Description("The list ID (from get_lists)")] string listId,
        CancellationToken ct = default)
    {
        var items = await api.GetItemsAsync(listId, ct);

        if (items.Count == 0)
            return "List is empty.";

        return string.Join("\n", items.Select(i =>
        {
            var check = i.Checked ? "[x]" : "[ ]";
            var qty = string.IsNullOrWhiteSpace(i.Amount)
                ? ""
                : $" ({i.Amount}{(string.IsNullOrWhiteSpace(i.Unit) ? "" : " " + i.Unit)})";
            return $"{check} [{i.Id}] {i.Name}{qty}";
        }));
    }

    [McpServerTool(Name = "add_items")]
    [Description("Add one or more items to a list. Batch all additions in a single call.")]
    public async Task<string> AddItemsAsync(
        [Description("The list ID (from get_lists)")] string listId,
        [Description("Items to add")] IEnumerable<AddItemInput> items,
        CancellationToken ct = default)
    {
        var added = new List<string>();
        foreach (var item in items)
        {
            var result = await api.AddItemAsync(listId, new AddItemRequest
            {
                Name = item.Name,
                Quantity = item.Amount,
                Unit = item.Unit,
            }, ct);
            added.Add($"Added [{result.Id}] {result.Name}");
        }
        return added.Count == 0 ? "No items added." : string.Join("\n", added);
    }

    [McpServerTool(Name = "edit_items")]
    [Description("Edit one or more items on a list (PATCH semantics — null fields are not changed).")]
    public async Task<string> EditItemsAsync(
        [Description("The list ID (from get_lists)")] string listId,
        [Description("Items to edit")] IEnumerable<EditItemInput> items,
        CancellationToken ct = default)
    {
        int count = 0;
        foreach (var item in items)
        {
            await api.UpdateItemAsync(listId, item.ItemId, new UpdateItemRequest
            {
                IsChecked = item.IsChecked,
                Name = item.Name,
                Quantity = item.Quantity,
            }, ct);
            count++;
        }
        return $"Updated {count} item(s).";
    }

    [McpServerTool(Name = "delete_items")]
    [Description("Permanently remove one or more items from a list (not check off — use edit_items for that).")]
    public async Task<string> DeleteItemsAsync(
        [Description("The list ID (from get_lists)")] string listId,
        [Description("Item IDs to delete (from get_items)")] IEnumerable<string> itemIds,
        CancellationToken ct = default)
    {
        int count = 0;
        foreach (var itemId in itemIds)
        {
            await api.DeleteItemAsync(listId, itemId, ct);
            count++;
        }
        return $"Deleted {count} item(s).";
    }
}
