using System.Text.Json.Serialization;
using ListonicApiClient.Converters;

namespace ListonicApiClient.Models.Items;

public class AddItemRequest
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("amount")]
    [JsonConverter(typeof(DecimalStringConverter))]
    public decimal? Quantity { get; init; }

    [JsonPropertyName("unit")]
    public string? Unit { get; init; }

    [JsonPropertyName("price")]
    public decimal? Price { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("categoryId")]
    public int? CategoryId { get; init; }
}
