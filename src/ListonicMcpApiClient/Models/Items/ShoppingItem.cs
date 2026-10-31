namespace ListonicMcpApiClient.Models.Items;

public class ShoppingItem
{
    public string Id { get; init; } = string.Empty;
    public int IdAsNumber { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Checked { get; init; } = string.Empty;
    public string Amount { get; init; } = string.Empty;
    public string Unit { get; init; } = string.Empty;
    public float Price { get; init; }
    public string Description { get; init; } = string.Empty;
    public int CategoryId { get; init; }
}
