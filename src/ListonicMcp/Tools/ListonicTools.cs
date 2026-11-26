using System.ComponentModel;
using ListonicApiClient.Models.Items;
using ListonicApiClient.Models.Lists;
using ListonicApiClient;
using ModelContextProtocol.Server;

namespace ListonicMcp.Tools;

[McpServerToolType]
public sealed class ListonicTools(IListonicApi api, ILogger<ListonicTools> logger)
{
    /// <summary>Tags the current tools/call span with the target list.</summary>
    private static void TagList(string listId, string listName)
    {
        ListonicTelemetry.Tag(ListonicTelemetry.Tags.ListId, listId);
        ListonicTelemetry.Tag(ListonicTelemetry.Tags.ListName, listName);
    }

    public record AddItemInput(
        [property: Description("Product name in the user's language, e.g. \"mleko\". No quantity inside the name.")] string Name,
        [property: Description("Quantity as a number, e.g. 2, 0.5, 500. Leave null if no quantity.")] decimal? Amount = null,
        [property: Description("Free-text unit shown in the app, e.g. 'kg', 'g', 'l', 'szt.'. Leave null for plain counts or no quantity.")] string? Unit = null);

    public record EditItemInput(
        [property: Description("Item ID (from get_items)")] string ItemId,
        [property: Description("The item's current name exactly as get_items shows it - when several rows share that name, add the quantity, e.g. \"kefir (0.5 l)\", and \"[x]\" for a checked row if that's still ambiguous. Shown in the permission prompt so the user can confirm the right item; not sent to the API. Use the current name even when renaming.")] string ConfirmName,
        [property: Description("true = bought / ticked off, false = back to still to buy")] bool? IsChecked = null,
        [property: Description("New item name")] string? Name = null,
        [property: Description("New amount as a number, e.g. 2, 0.5. Send 0 to clear. Leave null to keep existing.")] decimal? Amount = null,
        [property: Description("Free-text unit shown in the app, e.g. 'kg', 'g', 'l', 'szt.'. Send \"\" to clear. Leave null to keep existing.")] string? Unit = null);

    public record DeleteItemInput(
        [property: Description("Item ID (from get_items)")] string ItemId,
        [property: Description("The item's current name exactly as get_items shows it - when several rows share that name, add the quantity, e.g. \"kefir (0.5 l)\", and \"[x]\" for a checked row if that's still ambiguous. Shown in the permission prompt so the user can confirm the right item; not sent to the API.")] string ConfirmName);

    [McpServerTool(Name = "get_lists", Title = "Get shopping lists", ReadOnly = true, Idempotent = true)]
    [Description("List every shopping list the user can access (own and shared). Returns one line per list as \"[listId] name (private|shared) - N unchecked\". Call this first - every other tool except create_list needs a listId from here. Match the user's wording to a list name; if more than one list could fit, ask which one. This server has no categories or item notes and can't move items between lists - say so instead of looking for another tool.")]
    public Task<string> GetListsAsync(CancellationToken ct = default)
        => ToolErrors.GuardAsync("get_lists", logger, async () =>
    {
        var lists = await api.GetListsAsync(new GetListsRequest
        {
            IncludeShares = true,
            Archive = false,
            IncludeItems = false,
        }, ct);

        ListonicTelemetry.Tag(ListonicTelemetry.Tags.ListsCount, lists.Count);
        ListonicTelemetry.Outcome("ok");
        logger.LogInformation("get_lists: {Count} list(s)", lists.Count);

        if (lists.Count == 0)
            return "No lists found.";

        return string.Join("\n", lists.Select(l =>
        {
            var shared = l.Shares.Count > 1 ? "shared" : "private";
            var unchecked_ = l.ItemsCount - l.CheckedItemsCount;
            return $"[{l.Id}] {l.Name} ({shared}) - {unchecked_} unchecked";
        }));
    });

    [McpServerTool(Name = "get_items", Title = "Get list items", ReadOnly = true, Idempotent = true)]
    [Description(
        "Get every item on one list. Unchecked items (still to buy) come first, then checked ones (bought / ticked off), each as \"[ ] [itemId] name (amount unit)\" or \"[x] …\". " +
        "When the user asks what to buy, report only unchecked items; mention checked ones only if asked. " +
        "Call this before edit_items or delete_items to get itemIds, and before add_items to avoid duplicates. " +
        "Several rows can share a name. When the user names such an item, tick or untick every row the change applies to (only unchecked rows when ticking off). Before deleting it or changing its amount, consider only unchecked rows unless the user means a checked one: if one row matches, act on it; if several match, ask which one.")]
    public Task<string> GetItemsAsync(
        [Description("The list ID (from get_lists)")] string listId,
        [Description("The list's name exactly as get_lists shows it. Shown in the permission prompt so the user can confirm the right list; not sent to the API.")] string listName,
        CancellationToken ct = default)
        => ToolErrors.GuardAsync("get_items", logger, async () =>
    {
        TagList(listId, listName);
        var items = await api.GetItemsAsync(listId, ct);
        ListonicTelemetry.Tag(ListonicTelemetry.Tags.ItemsCount, items.Count);
        ListonicTelemetry.Outcome("ok");
        logger.LogInformation("get_items [{ListId}] {ListName}: {Count} item(s), {Unchecked} unchecked",
            listId, listName, items.Count, items.Count(i => !i.Checked));

        if (items.Count == 0)
        {
            // Listonic answers 200 [] for an unknown list id too; GET /lists/{id} 404s for those
            // (mapped to "not found" by the guard), so a wrong id isn't reported as an empty list.
            await api.GetListAsync(listId, ct);
            return "List is empty.";
        }

        return string.Join("\n", items.OrderBy(i => i.Checked ? 1 : 0).Select(FormatItem));
    });

    [McpServerTool(Name = "add_items", Title = "Add items", ReadOnly = false, Destructive = false, Idempotent = false)]
    [Description(
        "Add one or more items to a list in a single call; new items start unchecked. " +
        "Put only the product in name (in the user's language, e.g. \"mleko\"), the number in amount and the unit in unit - never the quantity inside the name. " +
        "Before adding, call get_items and check whether the item already exists. If it's checked, untick it and set the amount the user gave. If it's unchecked, ask whether to add to its amount or replace it. Don't create a duplicate row. " +
        "Returns the added items in get_items format.")]
    public Task<string> AddItemsAsync(
        [Description("The list ID (from get_lists)")] string listId,
        [Description("The list's name exactly as get_lists shows it. Shown in the permission prompt so the user can confirm the right list; not sent to the API.")] string listName,
        [Description("Items to add")] List<AddItemInput> items,
        CancellationToken ct = default)
        => ToolErrors.GuardAsync("add_items", logger, async () =>
    {
        TagList(listId, listName);
        ListonicTelemetry.Tag(ListonicTelemetry.Tags.ItemsCount, items.Count);
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

        ListonicTelemetry.Outcome("added");
        logger.LogInformation("add_items [{ListId}] {ListName}: added {Count} item(s)", listId, listName, addedIds.Count);

        if (addedIds.Count == 0)
            return "No items added.";

        var allItems = await api.GetItemsAsync(listId, ct);
        var added = allItems.Where(i => addedIds.Contains(i.Id)).ToList();
        return added.Count > 0
            ? string.Join("\n", added.Select(FormatItem))
            : string.Join("\n", addedIds.Select(id => $"[ ] [{id}]"));
    });

    [McpServerTool(Name = "edit_items", Title = "Edit items (tick off, rename, amount)", ReadOnly = false, Destructive = false, Idempotent = true)]
    [Description(
        "Change items already on a list: tick them off when bought (isChecked: true), untick (false), rename, or change amount/unit. " +
        "\"I bought X\" = tick it off, do not delete. " +
        "Send only the fields to change; omitted or null fields stay as they are; send amount 0 and unit \"\" to clear the quantity. " +
        "Get itemIds from get_items. Items can't be moved between lists - add to target list and delete from source. " +
        "Batch: send all items to change in one call. Returns the updated items in get_items format.")]
    public Task<string> EditItemsAsync(
        [Description("The list ID (from get_lists)")] string listId,
        [Description("The list's name exactly as get_lists shows it. Shown in the permission prompt so the user can confirm the right list; not sent to the API.")] string listName,
        [Description("Items to edit")] List<EditItemInput> items,
        CancellationToken ct = default)
        => ToolErrors.GuardAsync("edit_items", logger, async () =>
    {
        TagList(listId, listName);
        ListonicTelemetry.Tag(ListonicTelemetry.Tags.ItemsCount, items.Count);
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

        ListonicTelemetry.Outcome("edited");
        logger.LogInformation("edit_items [{ListId}] {ListName}: edited {Count} item(s)", listId, listName, editedIds.Count);

        if (editedIds.Count == 0)
            return "No items edited.";

        var allItems = await api.GetItemsAsync(listId, ct);
        var updated = allItems.Where(i => editedIds.Contains(i.Id)).ToList();
        return updated.Count > 0
            ? string.Join("\n", updated.Select(FormatItem))
            : $"Updated {editedIds.Count} item(s).";
    });

    [McpServerTool(Name = "delete_items", Title = "Delete items", ReadOnly = false, Destructive = true, Idempotent = false)]
    [Description(
        "Permanently delete items from a list; this can't be undone. To mark something as bought, tick it off with edit_items instead. " +
        "Use this when the user wants an item gone (added by mistake, no longer needed) or asks to clear bought items - then delete only the checked ones. " +
        "Before deleting more than a few items or emptying a whole list, confirm with the user. " +
        "Get itemIds from get_items. Always pass confirmName alongside each itemId. Returns the deleted items in get_items format.")]
    public Task<string> DeleteItemsAsync(
        [Description("The list ID (from get_lists)")] string listId,
        [Description("The list's name exactly as get_lists shows it. Shown in the permission prompt so the user can confirm the right list; not sent to the API.")] string listName,
        [Description("Items to delete (from get_items)")] List<DeleteItemInput> items,
        CancellationToken ct = default)
        => ToolErrors.GuardAsync("delete_items", logger, async () =>
    {
        TagList(listId, listName);
        ListonicTelemetry.Tag(ListonicTelemetry.Tags.ItemsCount, items.Count);
        var allItems = await api.GetItemsAsync(listId, ct);
        var itemById = allItems.ToDictionary(i => i.Id);

        // Listonic answers 200 to DELETE of an id that isn't on the list, so check against the list
        // first and report missing ids instead of claiming they were deleted.
        var deleted = new List<string>();
        var missing = new List<string>();
        foreach (var item in items)
        {
            if (!itemById.TryGetValue(item.ItemId, out var si))
            {
                missing.Add($"[{item.ItemId}] {item.ConfirmName}");
                continue;
            }
            await api.DeleteItemAsync(listId, item.ItemId, ct);
            deleted.Add(FormatItem(si));
        }
        ListonicTelemetry.Outcome("deleted");
        logger.LogInformation("delete_items [{ListId}] {ListName}: deleted {Count} item(s), {Missing} not on the list",
            listId, listName, deleted.Count, missing.Count);

        var result = deleted.Count > 0 ? string.Join("\n", deleted) : "No items deleted.";
        if (missing.Count > 0)
            result += "\nNot on this list (nothing deleted; call get_items for current ids):\n" + string.Join("\n", missing);
        return result;
    });

    [McpServerTool(Name = "create_list", Title = "Create list", ReadOnly = false, Destructive = false, Idempotent = false)]
    [Description(
        "Create a new, empty shopping list owned by the user (private until they share it in the app). " +
        "Call get_lists first: if a list with that name already exists, use it or ask the user instead of creating a duplicate. " +
        "Returns the new list in get_lists format; add items to it with add_items using its listId.")]
    public Task<string> CreateListAsync(
        [Description("Name for the new list, in the user's language, e.g. \"Zakupy na weekend\".")] string name,
        CancellationToken ct = default)
        => ToolErrors.GuardAsync("create_list", logger, async () =>
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ModelContextProtocol.McpException("The list name can't be empty.");

        var list = await api.CreateListAsync(new CreateListRequest { Name = name.Trim() }, ct);
        TagList(list.Id, list.Name);
        ListonicTelemetry.Outcome("created");
        logger.LogInformation("create_list [{ListId}] {ListName}", list.Id, list.Name);
        return $"[{list.Id}] {(string.IsNullOrEmpty(list.Name) ? name.Trim() : list.Name)} (private) - 0 unchecked";
    });

    [McpServerTool(Name = "rename_list", Title = "Rename list", ReadOnly = false, Destructive = false, Idempotent = true)]
    [Description("Rename an existing shopping list. On a shared list the new name is visible to everyone who shares it. To make a new list use create_list.")]
    public Task<string> RenameListAsync(
        [Description("The list ID (from get_lists)")] string listId,
        [Description("The list's current name exactly as get_lists shows it (not the new name). Shown in the permission prompt so the user can confirm the right list; not sent to the API.")] string listName,
        [Description("New name for the list")] string name,
        CancellationToken ct = default)
        => ToolErrors.GuardAsync("rename_list", logger, async () =>
    {
        TagList(listId, listName);
        // Listonic answers 200 to a rename of an unknown list id; GET /lists/{id} 404s for those
        // (mapped to "not found" by the guard), so a wrong id isn't reported as renamed.
        await api.GetListAsync(listId, ct);
        await api.RenameListAsync(listId, new RenameListRequest { Id = listId, Name = name }, ct);
        ListonicTelemetry.Outcome("renamed");
        logger.LogInformation("rename_list [{ListId}] {ListName} -> {NewName}", listId, listName, name);
        return $"List {listId} renamed to \"{name}\".";
    });

    [McpServerTool(Name = "delete_list", Title = "Delete list (to trash)", ReadOnly = false, Destructive = true, Idempotent = false)]
    [Description("Move a whole shopping list to the Listonic trash; it can be restored from the trash in the app. On a shared list this removes it for everyone who shares it - get_lists shows whether a list is shared. Always confirm with the user, naming the list, before calling. To delete items but keep the list, use delete_items instead.")]
    public Task<string> DeleteListAsync(
        [Description("The list ID (from get_lists)")] string listId,
        [Description("The list's name exactly as get_lists shows it. Shown in the permission prompt so the user can confirm the right list; not sent to the API.")] string listName,
        CancellationToken ct = default)
        => ToolErrors.GuardAsync("delete_list", logger, async () =>
    {
        TagList(listId, listName);
        await api.DeleteListAsync(listId, ct);
        ListonicTelemetry.Outcome("deleted");
        logger.LogInformation("delete_list [{ListId}] {ListName} moved to trash", listId, listName);
        return $"List {listId} moved to trash.";
    });

    private static string FormatItem(ShoppingItem i)
    {
        var check = i.Checked ? "[x]" : "[ ]";
        var hasAmount = !string.IsNullOrWhiteSpace(i.Amount) && i.Amount != "0";
        var qty = hasAmount
            ? $" ({i.Amount}{(string.IsNullOrWhiteSpace(i.Unit) ? "" : " " + i.Unit)})"
            : "";
        return $"{check} [{i.Id}] {i.Name}{qty}";
    }
}
