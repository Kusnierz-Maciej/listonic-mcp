namespace ListonicApiClient.Models.Items;

public class AddItemRequest
{
    public string Name { get; init; } = string.Empty;
    public string? Quantity { get; init; }
    public string? Unit { get; init; }
    public string? Price { get; init; }
    public string? Description { get; init; }
    public int? CategoryId { get; init; }
}
