using ListonicApiClient.Models.Items;
using ListonicApiClient.Models.Lists;

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
    public async Task RenameList_UpdatesName()
    {
        var created = await _api.CreateListAsync(new CreateListRequest { Name = TestPrefix + "RenameList" });

        await _api.RenameListAsync(created.Id, new RenameListRequest { Id = created.Id, Name = TestPrefix + "RenameList-Renamed" });

        var list = await _api.GetListAsync(created.Id);
        await Assert.That(list.Name).IsEqualTo(TestPrefix + "RenameList-Renamed");
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

        var item = await _api.AddItemAsync(list.Id, new AddItemRequest { Name = "mleko" });

        await Assert.That(item.Id).IsNotEmpty();
        await Assert.That(item.Name).IsEqualTo("mleko");
    }

    [Test]
    public async Task AddItem_WithAllFields_PreservesFields()
    {
        var list = await _api.CreateListAsync(new CreateListRequest { Name = TestPrefix + "AddItemFull" });

        var added = await _api.AddItemAsync(list.Id, new AddItemRequest
        {
            Name        = "jajka",
            Quantity    = "12",
            Unit        = "szt",
            Description = "od kur z wolnego wybiegu"
        });

        // Re-fetch — the add response doesn't always populate all fields
        var item = (await _api.GetItemsAsync(list.Id)).Single(i => i.Id == added.Id);

        await Assert.That(item.Name).IsEqualTo("jajka");
        await Assert.That(item.Amount).IsEqualTo("12");
        await Assert.That(item.Unit).IsEqualTo("szt");
        await Assert.That(item.Description).IsEqualTo("od kur z wolnego wybiegu");
    }

    [Test]
    public async Task UpdateItem_Name_UpdatesSuccessfully()
    {
        var list = await _api.CreateListAsync(new CreateListRequest { Name = TestPrefix + "UpdateItemName" });
        var item = await _api.AddItemAsync(list.Id, new AddItemRequest { Name = "chleb pszenny" });

        await _api.UpdateItemAsync(list.Id, item.Id, new UpdateItemRequest { Name = "chleb żytni" });

        var items = await _api.GetItemsAsync(list.Id);
        await Assert.That(items.Single(i => i.Id == item.Id).Name).IsEqualTo("chleb żytni");
    }

    [Test]
    public async Task UpdateItem_IsChecked_TogglesChecked()
    {
        var list = await _api.CreateListAsync(new CreateListRequest { Name = TestPrefix + "UpdateItemCheck" });
        var item = await _api.AddItemAsync(list.Id, new AddItemRequest { Name = "masło" });

        await _api.UpdateItemAsync(list.Id, item.Id, new UpdateItemRequest { IsChecked = true });

        var items = await _api.GetItemsAsync(list.Id);
        await Assert.That(items.Single(i => i.Id == item.Id).Checked).IsTrue();
    }

    [Test]
    public async Task DeleteItem_RemovesFromItems()
    {
        var list = await _api.CreateListAsync(new CreateListRequest { Name = TestPrefix + "DeleteItem" });
        var item = await _api.AddItemAsync(list.Id, new AddItemRequest { Name = "cukier" });

        await _api.DeleteItemAsync(list.Id, item.Id);

        var items = await _api.GetItemsAsync(list.Id);
        await Assert.That(items.Any(i => i.Id == item.Id)).IsFalse();
    }

    [Test]
    public async Task UpdateItem_WithNullFields_DoesNotClearExistingValues()
    {
        var list = await _api.CreateListAsync(new CreateListRequest { Name = TestPrefix + "UpdateItemNulls" });
        var item = await _api.AddItemAsync(list.Id, new AddItemRequest
        {
            Name     = "ser żółty",
            Quantity = "500",
            Unit     = "g"
        });
        await _api.UpdateItemAsync(list.Id, item.Id, new UpdateItemRequest { IsChecked = true });

        // Patch only Name — IsChecked, Quantity and Unit are null in the request
        await _api.UpdateItemAsync(list.Id, item.Id, new UpdateItemRequest { Name = "ser gouda" });

        var updated = (await _api.GetItemsAsync(list.Id)).Single(i => i.Id == item.Id);
        await Assert.That(updated.Name).IsEqualTo("ser gouda");
        await Assert.That(updated.Checked).IsTrue();   // must not be reset to false by null IsChecked
        await Assert.That(updated.Amount).IsEqualTo("500"); // must not be cleared by null Quantity
        await Assert.That(updated.Unit).IsEqualTo("g");
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
