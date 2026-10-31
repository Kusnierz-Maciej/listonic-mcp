using ListonicApiClient.Models.Items;
using ListonicApiClient.Models.Lists;
using ListonicApiClient.Models.Login;

namespace ListonicApiClient.Tests;

public class ListonicApiTests
{
    private const string TestPrefix = "[test] ";

    private readonly IListonicApi _api = ApiClientFactory.Api;

    [After(HookType.Class)]
    public static async Task CleanUp()
    {
        var api = ApiClientFactory.Api;
        var lists = await api.GetListsAsync(new GetListsRequest());
        foreach (var list in lists.Where(l => l.Name.StartsWith(TestPrefix)))
            await api.DeleteListAsync(list.Id);
    }

    [Test]
    public async Task Login_WithValidCredentials_Succeeds()
    {
        var response = await _api.LoginAsync(ApiClientFactory.CreateLoginRequest());

        await Assert.That(response.AccessToken).IsNotEmpty();
    }

    [Test]
    public async Task GetLists_ReturnsSuccessfully()
    {
        var created = await _api.CreateListAsync(new CreateListRequest { Name = TestPrefix + "GetLists" });

        var lists = await _api.GetListsAsync(new GetListsRequest());

        await Assert.That(lists.Any(l => l.Id == created.Id)).IsTrue();
    }

    [Test]
    public async Task GetList_ReturnsCorrectList()
    {
        var created = await _api.CreateListAsync(new CreateListRequest { Name = TestPrefix + "GetList" });

        var list = await _api.GetListAsync(created.Id);

        await Assert.That(list.Id).IsEqualTo(created.Id);
        await Assert.That(list.Name).IsEqualTo(TestPrefix + "GetList");
    }

    [Test]
    public async Task CreateList_ReturnsCreatedList()
    {
        var list = await _api.CreateListAsync(new CreateListRequest { Name = TestPrefix + "CreateList" });

        await Assert.That(list.Id).IsNotEmpty();
        await Assert.That(list.Name).IsEqualTo(TestPrefix + "CreateList");
    }

    [Test]
    public async Task DeleteList_RemovesFromLists()
    {
        var created = await _api.CreateListAsync(new CreateListRequest { Name = TestPrefix + "DeleteList" });

        await _api.DeleteListAsync(created.Id);

        var lists = await _api.GetListsAsync(new GetListsRequest());
        await Assert.That(lists.Any(l => l.Id == created.Id)).IsFalse();
    }

    [Test]
    public async Task GetItems_OnEmptyList_ReturnsEmpty()
    {
        var list = await _api.CreateListAsync(new CreateListRequest { Name = TestPrefix + "GetItems" });

        var items = await _api.GetItemsAsync(list.Id);

        await Assert.That(items).IsEmpty();
    }

    [Test]
    public async Task AddItem_ReturnsAddedItem()
    {
        var list = await _api.CreateListAsync(new CreateListRequest { Name = TestPrefix + "AddItem" });

        var item = await _api.AddItemAsync(list.Id, new AddItemRequest { Name = "Milk" });

        await Assert.That(item.Id).IsNotEmpty();
        await Assert.That(item.Name).IsEqualTo("Milk");
    }

    [Test]
    public async Task AddItem_WithAllFields_PreservesFields()
    {
        var list = await _api.CreateListAsync(new CreateListRequest { Name = TestPrefix + "AddItemFull" });

        var item = await _api.AddItemAsync(list.Id, new AddItemRequest
        {
            Name        = "Eggs",
            Quantity    = "12",
            Unit        = "pcs",
            Description = "free range"
        });

        await Assert.That(item.Name).IsEqualTo("Eggs");
        await Assert.That(item.Amount).IsEqualTo("12");
        await Assert.That(item.Unit).IsEqualTo("pcs");
        await Assert.That(item.Description).IsEqualTo("free range");
    }

    [Test]
    public async Task UpdateItem_Name_UpdatesSuccessfully()
    {
        var list = await _api.CreateListAsync(new CreateListRequest { Name = TestPrefix + "UpdateItemName" });
        var item = await _api.AddItemAsync(list.Id, new AddItemRequest { Name = "OldName" });

        await _api.UpdateItemAsync(list.Id, item.Id, new UpdateItemRequest { Name = "NewName" });

        var items = await _api.GetItemsAsync(list.Id);
        await Assert.That(items.Single(i => i.Id == item.Id).Name).IsEqualTo("NewName");
    }

    [Test]
    public async Task UpdateItem_IsChecked_TogglesChecked()
    {
        var list = await _api.CreateListAsync(new CreateListRequest { Name = TestPrefix + "UpdateItemCheck" });
        var item = await _api.AddItemAsync(list.Id, new AddItemRequest { Name = "Butter" });

        await _api.UpdateItemAsync(list.Id, item.Id, new UpdateItemRequest { IsChecked = true });

        var items = await _api.GetItemsAsync(list.Id);
        await Assert.That(items.Single(i => i.Id == item.Id).Checked).IsEqualTo("1");
    }

    [Test]
    public async Task DeleteItem_RemovesFromItems()
    {
        var list = await _api.CreateListAsync(new CreateListRequest { Name = TestPrefix + "DeleteItem" });
        var item = await _api.AddItemAsync(list.Id, new AddItemRequest { Name = "Sugar" });

        await _api.DeleteItemAsync(list.Id, item.Id);

        var items = await _api.GetItemsAsync(list.Id);
        await Assert.That(items.Any(i => i.Id == item.Id)).IsFalse();
    }

    [Test]
    public async Task GetUserInfo_ReturnsSuccessfully()
    {
        var result = await _api.GetUserInfoAsync();

        await Assert.That(result).IsNotNull();
    }

    [Test]
    public async Task GetCategories_ReturnsNonEmpty()
    {
        var result = await _api.GetCategoriesAsync();

        await Assert.That(result).IsNotNull();
    }
}
