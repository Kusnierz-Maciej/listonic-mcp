namespace ListonicApiClient.Models.Items;

public class UpdateItemRequest
{
    public bool? IsChecked { get; init; }
    public string? Name { get; init; }
    public string? Quantity { get; init; }
}
