using System.Text.Json.Serialization;

namespace ListonicApiClient.Models.Items;

public class AddItemRequest
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("amount")]
    public string? Quantity { get; init; }

    [JsonPropertyName("unit")]
    public string? Unit { get; init; }

    [JsonPropertyName("price")]
    public string? Price { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("categoryId")]
    public int? CategoryId { get; init; }
}
