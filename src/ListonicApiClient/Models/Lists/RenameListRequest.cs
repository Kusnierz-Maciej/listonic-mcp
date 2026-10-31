using System.Text.Json.Serialization;

namespace ListonicApiClient.Models.Lists;

public class RenameListRequest
{
    [JsonPropertyName("Id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("Name")]
    public string Name { get; init; } = string.Empty;
}
