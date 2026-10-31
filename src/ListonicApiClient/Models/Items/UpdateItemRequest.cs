using System.Text.Json.Serialization;
using ListonicApiClient.Converters;

namespace ListonicApiClient.Models.Items;

public class UpdateItemRequest
{
    [JsonPropertyName("checked")]
    [JsonConverter(typeof(ZeroOneBoolConverter))]
    public bool? IsChecked { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("quantity")]
    public string? Quantity { get; init; }
}
