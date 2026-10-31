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

    [JsonPropertyName("amount")]
    [JsonConverter(typeof(DecimalStringConverter))]
    public decimal? Amount { get; init; }

    [JsonPropertyName("unit")]
    public string? Unit { get; init; }
}
