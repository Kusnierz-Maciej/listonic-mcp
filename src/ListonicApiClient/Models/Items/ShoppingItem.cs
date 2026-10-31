using System.Text.Json.Serialization;
using ListonicApiClient.Converters;

namespace ListonicApiClient.Models.Items;

public class ShoppingItem
{
    public string Id { get; init; } = string.Empty;
    public long IdAsNumber { get; init; }
    public string Name { get; init; } = string.Empty;

    [JsonConverter(typeof(ZeroOneBoolConverter))]
    public bool Checked { get; init; }

    public string Amount { get; init; } = string.Empty;
    public string Unit { get; init; } = string.Empty;
    public string Price { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public long CategoryId { get; init; }
}
