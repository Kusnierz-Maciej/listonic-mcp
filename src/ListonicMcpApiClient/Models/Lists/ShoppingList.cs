using ListonicMcpApiClient.Models.Items;

namespace ListonicMcpApiClient.Models.Lists;

public class ShoppingList
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int Active { get; init; }
    public int Deleted { get; init; }
    public List<ShoppingItem> Items { get; init; } = [];
}
