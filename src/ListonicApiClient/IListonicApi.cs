using ListonicApiClient.Models.Login;
using Refit;
using ListonicApiClient.Models.Items;
using ListonicApiClient.Models.Lists;

namespace ListonicApiClient;

public interface IListonicApi
{
    /// <summary>Authenticates with email/password. Token TTL is ~24h; <see cref="ListonicAuthHandler"/> handles renewal transparently.</summary>
    [Post("/loginextended?provider=password&autoMerge=1&autoDestruct=1")]
    [Headers("clientauthorization: Bearer bGlzdG9uaWN2MjpmamRmc29qOTg3NGpkZmhqa2gzNGpraGZmZGZmZg==")]
    Task<LoginResponse> LoginAsync([Body(BodySerializationMethod.UrlEncoded)] LoginRequest request, CancellationToken ct = default);

    /// <summary>Returns all lists visible to the account. Use <see cref="GetListsRequest"/> to control archive/shares/items inclusion.</summary>
    [Get("/lists")]
    Task<List<ShoppingList>> GetListsAsync([Query] GetListsRequest request, CancellationToken ct = default);

    /// <summary>Returns a single list by its string ID.</summary>
    [Get("/lists/{listId}")]
    Task<ShoppingList> GetListAsync(string listId, CancellationToken ct = default);

    /// <summary>Creates a new list. Returns the created list including its assigned ID.</summary>
    [Post("/lists")]
    Task<ShoppingList> CreateListAsync([Body] CreateListRequest request, CancellationToken ct = default);

    /// <summary>Moves the list to trash (soft delete).</summary>
    [Delete("/lists/{listId}")]
    Task DeleteListAsync(string listId, CancellationToken ct = default);

    /// <summary>Renames a list.</summary>
    [Patch("/lists/{listId}")]
    Task RenameListAsync(string listId, [Body] RenameListRequest request, CancellationToken ct = default);

    /// <summary>Returns all items on a list. <see cref="ShoppingItem.Checked"/> indicates whether the item has been bought.</summary>
    [Get("/lists/{listId}/items")]
    Task<List<ShoppingItem>> GetItemsAsync(string listId, CancellationToken ct = default);

    /// <summary>Adds an item to a list. Note: the add response may not populate all fields — re-fetch via <see cref="GetItemsAsync"/> if needed.</summary>
    [Post("/lists/{listId}/items")]
    Task<ShoppingItem> AddItemAsync(string listId, [Body] AddItemRequest request, CancellationToken ct = default);

    /// <summary>Partially updates an item (PATCH semantics). Null fields in <see cref="UpdateItemRequest"/> are omitted and existing values are preserved.</summary>
    [Patch("/lists/{listId}/items/{itemId}")]
    Task UpdateItemAsync(string listId, string itemId, [Body] UpdateItemRequest request, CancellationToken ct = default);

    /// <summary>Permanently removes an item from the list.</summary>
    [Delete("/lists/{listId}/items/{itemId}")]
    Task DeleteItemAsync(string listId, string itemId, CancellationToken ct = default);

    /// <summary>Returns basic account info for the authenticated user.</summary>
    [Get("/account/userinfo")]
    Task<object> GetUserInfoAsync(CancellationToken ct = default);

    /// <summary>Returns product categories used to classify items (e.g. dairy, vegetables). Stable — safe to cache.</summary>
    [Get("/categoriesv2")]
    Task<object> GetCategoriesAsync(CancellationToken ct = default);
}
