using System.Text.Json.Serialization;
using ListonicApiClient.Models.Items;

namespace ListonicApiClient.Models.Lists;

public class ShoppingList
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int Active { get; init; }
    public int Deleted { get; init; }

    [JsonPropertyName("IsOwner")]
    public int IsOwner { get; init; }

    [JsonPropertyName("ItemsCount")]
    public int ItemsCount { get; init; }

    [JsonPropertyName("CheckedItemsCount")]
    public int CheckedItemsCount { get; init; }

    [JsonPropertyName("Shares")]
    public List<ListShare> Shares { get; init; } = [];

    public List<ShoppingItem> Items { get; init; } = [];
}

public class ListShare
{
    [JsonPropertyName("Email")]
    public string Email { get; init; } = string.Empty;

    [JsonPropertyName("Display")]
    public string Display { get; init; } = string.Empty;
}
