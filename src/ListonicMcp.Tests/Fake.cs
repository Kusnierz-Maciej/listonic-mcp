using ListonicApiClient.Models.Items;
using ListonicApiClient.Models.Lists;

namespace ListonicMcp.Tests;

/// <summary>Builders for API model objects used by the mocked <c>IListonicApi</c>.</summary>
internal static class Fake
{
    public static ShoppingList List(string id, string name, int items = 0, int checkedItems = 0, int shares = 1) => new()
    {
        Id = id,
        Name = name,
        ItemsCount = items,
        CheckedItemsCount = checkedItems,
        Shares = Enumerable.Range(0, shares).Select(i => new ListShare { Email = $"user{i}@example.com" }).ToList(),
    };

    public static ShoppingItem Item(string id, string name, bool isChecked = false, string amount = "", string unit = "") => new()
    {
        Id = id,
        Name = name,
        Checked = isChecked,
        Amount = amount,
        Unit = unit,
    };
}
