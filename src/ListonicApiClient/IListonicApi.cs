using ListonicApiClient.Models.Login;
using Refit;
using ListonicApiClient.Models.Items;
using ListonicApiClient.Models.Lists;

namespace ListonicApiClient;

public interface IListonicApi
{
    [Post("/loginextended?provider=password&autoMerge=1&autoDestruct=1")]
    [Headers("clientauthorization: Bearer bGlzdG9uaWN2MjpqZmRmc29qOTg3NGpkZmhqa2gzNGpraGZmZGZmZg==")]
    Task<LoginResponse> LoginAsync([Body(BodySerializationMethod.UrlEncoded)] LoginRequest request, CancellationToken ct = default);

    [Get("/lists")]
    Task<List<ShoppingList>> GetListsAsync([Query] GetListsRequest request, CancellationToken ct = default);

    [Get("/lists/{listId}")]
    Task<ShoppingList> GetListAsync(string listId, CancellationToken ct = default);

    [Post("/lists")]
    Task<ShoppingList> CreateListAsync([Body] CreateListRequest request, CancellationToken ct = default);

    [Delete("/lists/{listId}")]
    Task DeleteListAsync(string listId, CancellationToken ct = default);

    [Get("/lists/{listId}/items")]
    Task<List<ShoppingItem>> GetItemsAsync(string listId, CancellationToken ct = default);

    [Post("/lists/{listId}/items")]
    Task<ShoppingItem> AddItemAsync(string listId, [Body] AddItemRequest request, CancellationToken ct = default);

    [Patch("/lists/{listId}/items/{itemId}")]
    Task UpdateItemAsync(string listId, string itemId, [Body] UpdateItemRequest request, CancellationToken ct = default);

    [Delete("/lists/{listId}/items/{itemId}")]
    Task DeleteItemAsync(string listId, string itemId, CancellationToken ct = default);

    [Get("/account/userinfo")]
    Task<object> GetUserInfoAsync(CancellationToken ct = default);

    [Get("/categoriesv2")]
    Task<object> GetCategoriesAsync(CancellationToken ct = default);
}
