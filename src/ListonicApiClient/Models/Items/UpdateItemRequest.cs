using System.Text.Json.Serialization;

namespace ListonicApiClient.Models.Items;

public class UpdateItemRequest
{
    [JsonPropertyName("checked")]
    public int? IsChecked { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("quantity")]
    public string? Quantity { get; init; }
}
