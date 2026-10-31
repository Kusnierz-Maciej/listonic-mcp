using System.ComponentModel;
using ListonicApiClient.Models.Items;
using ListonicApiClient.Models.Lists;
using ListonicMcp.Listonic;
using ModelContextProtocol.Server;

namespace ListonicMcp.Tools;

[McpServerToolType]
public sealed class ListonicTools(ListonicApiAccessor accessor)
{
    public record AddItemInput(
        [property: Description("Product name in the user's language, e.g. \"mleko\". No quantity inside the name.")] string Name,
        [property: Description("Quantity as a number, e.g. 2, 0.5, 500 (dot as decimal separator). Leave null if no quantity.")] decimal? Amount = null,
        [property: Description("Free-text unit shown in the app, e.g. 'kg', 'g', 'l', 'szt.'. Leave null for plain counts or no quantity.")] string? Unit = null);

    public record EditItemInput(
        [property: Description("Item ID (from get_items)")] string ItemId,
        [property: Description("true = bought / ticked off, false = back to still to buy")] bool? IsChecked = null,
        [property: Description("New item name")] string? Name = null,
        [property: Description("New amount as a number, e.g. 2, 0.5 (dot as decimal separator). Send 0 to clear. Leave null to keep existing.")] decimal? Amount = null,
        [property: Description("Free-text unit shown in the app, e.g. 'kg', 'g', 'l', 'szt.'. Send \"\" to clear. Leave null to keep existing.")] string? Unit = null);

    [McpServerTool(Name = "get_lists")]
    [Description("List every shopping list the user can access (own and shared). Returns one line per list as \"[listId] name (private|shared) — N unchecked\". Call this first — every other tool needs a listId from here. Match the user's wording to a list name; if more than one list could fit, ask which one.")]
    public async Task<string> GetListsAsync(CancellationToken ct = default)
    {
        var api = accessor.Api;
        var lists = await api.GetListsAsync(new GetListsRequest
        {
            IncludeShares = true,
            Archive = false,
            IncludeItems = false,
        }, ct);

        if (lists.Count == 0)
            return "No lists found.";

        return string.Join("\n", lists.Select(l =>
        {
            var shared = l.Shares.Count > 1 ? "shared" : "private";
            var unchecked_ = l.ItemsCount - l.CheckedItemsCount;
            return $"[{l.Id}] {l.Name} ({shared}) — {unchecked_} unchecked";
        }));
    }

    [McpServerTool(Name = "get_items")]
    [Description("Get every item on one list, one per line as \"[ ] [itemId] name (amount unit)\" (unchecked, still to buy) or \"[x] …\" (checked: bought / ticked off in the app). When the user asks what to buy or what's on the list, report the unchecked items; mention checked ones only if asked. Call this before edit_items or delete_items to get itemIds, and before add_items to avoid adding an item that is already on the list. Items come in the app's order (new items near the top). Several rows can share a name; when the user names such an item, act on all of them or ask which one.")]
    public async Task<string> GetItemsAsync(
        [Description("The list ID (from get_lists)")] string listId,
        CancellationToken ct = default)
    {
        var items = await accessor.Api.GetItemsAsync(listId, ct);

        if (items.Count == 0)
            return "List is empty.";

        return string.Join("\n", items.Select(FormatItem));
    }

    [McpServerTool(Name = "add_items")]
    [Description("Add one or more items to a list in a single call; new items start unchecked. Put only the product in name (in the user's language, e.g. \"mleko\"), the number in amount and the unit in unit — never the quantity inside the name. If an item with the same name already exists, a second row is created — check get_items first and use edit_items to change its quantity or uncheck it instead. Returns the added items in get_items format.")]
    public async Task<string> AddItemsAsync(
        [Description("The list ID (from get_lists)")] string listId,
        [Description("Items to add")] List<AddItemInput> items,
        CancellationToken ct = default)
    {
        var api = accessor.Api;
        var addedIds = new List<string>();
        foreach (var item in items)
        {
            var result = await api.AddItemAsync(listId, new AddItemRequest
            {
                Name = item.Name,
                Quantity = item.Amount,
                Unit = item.Unit,
            }, ct);
            addedIds.Add(result.Id);
        }

        if (addedIds.Count == 0)
            return "No items added.";

        var allItems = await api.GetItemsAsync(listId, ct);
        var added = allItems.Where(i => addedIds.Contains(i.Id)).ToList();
        return added.Count > 0
            ? string.Join("\n", added.Select(FormatItem))
            : string.Join("\n", addedIds.Select(id => $"[ ] [{id}]"));
    }

    [McpServerTool(Name = "edit_items")]
    [Description("Change items already on a list: tick them off when bought (isChecked: true), untick them (false), rename them or change their amount/unit. Send only the fields to change; omitted or null fields stay as they are; send amount 0 and unit \"\" to clear the quantity. Get itemIds from get_items. Items can't be moved between lists: add them to the target list and delete them from the source. Returns the updated items in get_items format.")]
    public async Task<string> EditItemsAsync(
        [Description("The list ID (from get_lists)")] string listId,
        [Description("Items to edit")] List<EditItemInput> items,
        CancellationToken ct = default)
    {
        var api = accessor.Api;
        var editedIds = new List<string>();
        foreach (var item in items)
        {
            await api.UpdateItemAsync(listId, item.ItemId, new UpdateItemRequest
            {
                IsChecked = item.IsChecked,
                Name = item.Name,
                Amount = item.Amount,
                Unit = item.Unit,
            }, ct);
            editedIds.Add(item.ItemId);
        }

        if (editedIds.Count == 0)
            return "No items edited.";

        var allItems = await api.GetItemsAsync(listId, ct);
        var updated = allItems.Where(i => editedIds.Contains(i.Id)).ToList();
        return updated.Count > 0
            ? string.Join("\n", updated.Select(FormatItem))
            : $"Updated {editedIds.Count} item(s).";
    }

    [McpServerTool(Name = "delete_items")]
    [Description("Permanently delete items from a list; this can't be undone. To mark something as bought, tick it off with edit_items instead. Use this when the user wants an item gone (added by mistake, no longer needed) or asks to clear bought items — then delete only the checked ones. Before deleting more than a few items or emptying a whole list, confirm with the user. Get itemIds from get_items. Returns one line per deleted item: \"Deleted [itemId] name\".")]
    public async Task<string> DeleteItemsAsync(
        [Description("The list ID (from get_lists)")] string listId,
        [Description("Item IDs to delete (from get_items)")] List<string> itemIds,
        CancellationToken ct = default)
    {
        var api = accessor.Api;
        var allItems = await api.GetItemsAsync(listId, ct);
        var nameById = allItems.ToDictionary(i => i.Id, i => i.Name);

        var deleted = new List<string>();
        foreach (var itemId in itemIds)
        {
            await api.DeleteItemAsync(listId, itemId, ct);
            var name = nameById.TryGetValue(itemId, out var n) ? n : itemId;
            deleted.Add($"Deleted [{itemId}] {name}");
        }
        return deleted.Count > 0 ? string.Join("\n", deleted) : "No items deleted.";
    }

    [McpServerTool(Name = "rename_list")]
    [Description("Rename an existing shopping list. On a shared list the new name is visible to everyone who shares it. This server can't create or delete lists — tell the user to do that in the Listonic app.")]
    public async Task<string> RenameListAsync(
        [Description("The list ID (from get_lists)")] string listId,
        [Description("New name for the list")] string name,
        CancellationToken ct = default)
    {
        await accessor.Api.RenameListAsync(listId, new RenameListRequest { Id = listId, Name = name }, ct);
        return $"List {listId} renamed to \"{name}\".";
    }

    [McpServerTool(Name = "delete_list")]
    [Description("Move a shopping list to trash (soft delete). To delete list contents without removing the list, use delete_items instead.")]
    public async Task<string> DeleteListAsync(
        [Description("The list ID (from get_lists)")] string listId,
        CancellationToken ct = default)
    {
        await accessor.Api.DeleteListAsync(listId, ct);
        return $"List {listId} moved to trash.";
    }

    private static string FormatItem(ShoppingItem i)
    {
        var check = i.Checked ? "[x]" : "[ ]";
        var qty = string.IsNullOrWhiteSpace(i.Amount)
            ? ""
            : $" ({i.Amount}{(string.IsNullOrWhiteSpace(i.Unit) ? "" : " " + i.Unit)})";
        return $"{check} [{i.Id}] {i.Name}{qty}";
    }
}
