using ListonicApiClient;
using ListonicApiClient.Models.Lists;
using ListonicMcp.Tools;
using NSubstitute;

namespace ListonicMcp.Tests;

public class ToolOutputTests
{
    private readonly IListonicApi _api = Substitute.For<IListonicApi>();
    private ListonicTools Tools => new(_api);

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
