using System.Text.Json.Serialization;
using ListonicApiClient.Converters;

namespace ListonicApiClient.Models.Items;

public class UpdateItemRequest
{
    [JsonPropertyName("checked")]
    [JsonConverter(typeof(ZeroOneBoolConverter))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsChecked { get; init; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; init; }

    [JsonPropertyName("amount")]
    [JsonConverter(typeof(DecimalStringConverter))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? Amount { get; init; }

    [JsonPropertyName("unit")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Unit { get; init; }
}
