using ListonicApiClient;
using ListonicApiClient.Models.Lists;
using ListonicMcp.Tools;
using NSubstitute;

namespace ListonicMcp.Tests;

public class ToolOutputTests
{
    private readonly IListonicApi _api = Substitute.For<IListonicApi>();
    private ListonicTools Tools => new(_api, Microsoft.Extensions.Logging.Abstractions.NullLogger<ListonicTools>.Instance);

    [Test]
    public async Task GetLists_FormatsPrivateSharedAndUncheckedCount()
    {
        _api.GetListsAsync(Arg.Any<GetListsRequest>(), Arg.Any<CancellationToken>()).Returns(
        [
            Fake.List("1", "Zakupy", items: 5, checkedItems: 2, shares: 1),
            Fake.List("2", "Wspólna", items: 3, checkedItems: 0, shares: 2),
        ]);

        var result = await Tools.GetListsAsync();

        await Assert.That(result).IsEqualTo("[1] Zakupy (private) - 3 unchecked\n[2] Wspólna (shared) - 3 unchecked");
    }

    [Test]
    public async Task GetItems_UncheckedFirst_AndSuppressesZeroAmount()
    {
        _api.GetItemsAsync("1", Arg.Any<CancellationToken>()).Returns(
        [
            Fake.Item("a", "chleb", isChecked: true),
            Fake.Item("b", "mleko", amount: "2", unit: "l"),
            Fake.Item("c", "masło", amount: "0"),
        ]);

        var result = await Tools.GetItemsAsync("1", "Zakupy");

        await Assert.That(result).IsEqualTo("[ ] [b] mleko (2 l)\n[ ] [c] masło\n[x] [a] chleb");
    }

    [Test]
    public async Task GetItems_EmptyList()
    {
        _api.GetItemsAsync("1", Arg.Any<CancellationToken>()).Returns([]);

        await Assert.That(await Tools.GetItemsAsync("1", "Zakupy")).IsEqualTo("List is empty.");
    }
}

public class CreateListToolTests
{
    [Test]
    public async Task CreateList_ReturnsGetListsFormat_AndTrimsName()
    {
        var api = NSubstitute.Substitute.For<ListonicApiClient.IListonicApi>();
        api.CreateListAsync(NSubstitute.Arg.Is<CreateListRequest>(r => r != null && r.Name == "Weekend"), NSubstitute.Arg.Any<CancellationToken>())
            .Returns(Fake.List("42", "Weekend"));
        var tools = new ListonicTools(api, Microsoft.Extensions.Logging.Abstractions.NullLogger<ListonicTools>.Instance);

        await Assert.That(await tools.CreateListAsync("  Weekend ")).IsEqualTo("[42] Weekend (private) - 0 unchecked");
    }

    [Test]
    public async Task CreateList_EmptyName_IsRejected()
    {
        var api = NSubstitute.Substitute.For<ListonicApiClient.IListonicApi>();
        var tools = new ListonicTools(api, Microsoft.Extensions.Logging.Abstractions.NullLogger<ListonicTools>.Instance);

        await Assert.ThrowsAsync<ModelContextProtocol.McpException>(() => tools.CreateListAsync(" "));
        await api.DidNotReceiveWithAnyArgs().CreateListAsync(default!, default);
    }
}

public class SilentApiCasesTests
{
    private readonly ListonicApiClient.IListonicApi _api = NSubstitute.Substitute.For<ListonicApiClient.IListonicApi>();
    private ListonicTools Tools => new(_api, Microsoft.Extensions.Logging.Abstractions.NullLogger<ListonicTools>.Instance);

    [Test]
    public async Task GetItems_EmptyResult_ChecksListExists()
    {
        _api.GetItemsAsync("404", NSubstitute.Arg.Any<CancellationToken>()).Returns([]);
        var notFound = await Refit.ApiException.Create(
            new HttpRequestMessage(HttpMethod.Get, "https://api.listonic.com/api/lists/404"), HttpMethod.Get,
            new HttpResponseMessage(System.Net.HttpStatusCode.NotFound), new Refit.RefitSettings());
        NSubstitute.ExceptionExtensions.ExceptionExtensions.ThrowsAsync(
            _api.GetListAsync("404", NSubstitute.Arg.Any<CancellationToken>()), notFound);

        var ex = await Assert.ThrowsAsync<ModelContextProtocol.McpException>(() => Tools.GetItemsAsync("404", "Nope"));

        await Assert.That(ex!.Message).Contains("couldn't find that list or item");
    }

    [Test]
    public async Task DeleteItems_UnknownId_IsReported_NotDeleted()
    {
        _api.GetItemsAsync("1", NSubstitute.Arg.Any<CancellationToken>()).Returns([Fake.Item("a", "mleko")]);

        var result = await Tools.DeleteItemsAsync("1", "Zakupy",
            [new ListonicTools.DeleteItemInput("a", "mleko"), new ListonicTools.DeleteItemInput("zzz", "chleb")]);

        await Assert.That(result).IsEqualTo(
            "[ ] [a] mleko\nNot on this list (nothing deleted; call get_items for current ids):\n[zzz] chleb");
        await _api.Received(1).DeleteItemAsync("1", "a", NSubstitute.Arg.Any<CancellationToken>());
        await _api.DidNotReceive().DeleteItemAsync("1", "zzz", NSubstitute.Arg.Any<CancellationToken>());
    }
}
