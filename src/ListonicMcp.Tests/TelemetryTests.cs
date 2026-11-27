using System.Collections.Concurrent;
using System.Diagnostics;
using ListonicApiClient;
using ListonicApiClient.Models.Items;
using ListonicApiClient.Models.Lists;
using ListonicMcp.Tools;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace ListonicMcp.Tests;

/// <summary>
/// Span tagging: tools write list tags + outcome onto the current span (the MCP tools/call span in
/// production). A recorded parent activity stands in for that span; Activity.Current is async-local,
/// so parallel tests don't interfere.
/// </summary>
public class TelemetryTests
{
    private static readonly ActivitySource TestSource = new("ListonicMcp.Tests.Telemetry");
    private static readonly ConcurrentBag<Activity> Stopped = [];

    static TelemetryTests()
    {
        ActivitySource.AddActivityListener(new ActivityListener
        {
            // The test span stands in for the MCP tools/call span; the Listonic source records the phase spans.
            ShouldListenTo = s => s.Name == TestSource.Name || s.Name == ListonicTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a => Stopped.Add(a),
        });
    }

    /// <summary>Names of the child spans recorded under <paramref name="parent"/>.</summary>
    private static List<string> Phases(Activity parent) =>
        Stopped.Where(a => a.ParentSpanId == parent.SpanId && a.TraceId == parent.TraceId)
            .OrderBy(a => a.StartTimeUtc).Select(a => a.OperationName).ToList();

    private static ListonicTools Tools(IListonicApi api) => new(api, NullLogger<ListonicTools>.Instance);

    [Test]
    public async Task AddItems_TagsNamesIdsCounts_AndAddThenRereadPhases()
    {
        var api = Substitute.For<IListonicApi>();
        api.AddItemAsync("7", Arg.Any<AddItemRequest>(), Arg.Any<CancellationToken>())
            .Returns(Fake.Item("a1", "mleko"), Fake.Item("a2", "chleb"));
        api.GetItemsAsync("7", Arg.Any<CancellationToken>()).Returns([Fake.Item("a1", "mleko"), Fake.Item("a2", "chleb")]);

        using var span = TestSource.StartActivity("tools/call add_items")!;
        await Tools(api).AddItemsAsync("7", "Zakupy", [new("mleko", 2, "l"), new("chleb")]);
        span.Stop();

        await Assert.That((string[])span.GetTagItem(ListonicTelemetry.Tags.ItemNames)!).IsEquivalentTo(new[] { "mleko", "chleb" });
        await Assert.That((string[])span.GetTagItem(ListonicTelemetry.Tags.ItemIds)!).IsEquivalentTo(new[] { "a1", "a2" });
        await Assert.That(span.GetTagItem(ListonicTelemetry.Tags.ItemsAffected)).IsEqualTo(2);
        await Assert.That(span.GetTagItem(ListonicTelemetry.Tags.Outcome)).IsEqualTo("added");
        await Assert.That(Phases(span)).IsEquivalentTo(new[] { "listonic.items.add", "listonic.items.reread" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task EditItems_TagsWhatKindOfChange()
    {
        var api = Substitute.For<IListonicApi>();
        api.GetItemsAsync("7", Arg.Any<CancellationToken>()).Returns([Fake.Item("a", "mleko", isChecked: true)]);

        using var span = TestSource.StartActivity("tools/call edit_items")!;
        await Tools(api).EditItemsAsync("7", "Zakupy",
        [
            new("a", "mleko", IsChecked: true),
            new("b", "chleb", IsChecked: false, Amount: 2),
            new("c", "masło", Name: "masło extra"),
        ]);

        await Assert.That(span.GetTagItem(ListonicTelemetry.Tags.ItemsTicked)).IsEqualTo(1);
        await Assert.That(span.GetTagItem(ListonicTelemetry.Tags.ItemsUnticked)).IsEqualTo(1);
        await Assert.That(span.GetTagItem(ListonicTelemetry.Tags.ItemsRenamed)).IsEqualTo(1);
        await Assert.That(span.GetTagItem(ListonicTelemetry.Tags.ItemsQuantityChanged)).IsEqualTo(1);
    }

    [Test]
    public async Task DeleteItems_TagsAffectedAndMissing_AndLookupThenDeletePhases()
    {
        var api = Substitute.For<IListonicApi>();
        api.GetItemsAsync("7", Arg.Any<CancellationToken>()).Returns([Fake.Item("a", "mleko")]);

        using var span = TestSource.StartActivity("tools/call delete_items")!;
        await Tools(api).DeleteItemsAsync("7", "Zakupy", [new("a", "mleko"), new("zzz", "chleb")]);
        span.Stop();

        await Assert.That(span.GetTagItem(ListonicTelemetry.Tags.ItemsAffected)).IsEqualTo(1);
        await Assert.That(span.GetTagItem(ListonicTelemetry.Tags.ItemsMissing)).IsEqualTo(1);
        await Assert.That(Phases(span)).IsEquivalentTo(new[] { "listonic.items.lookup", "listonic.items.delete" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task Capped_LimitsListAttributes()
    {
        var many = Enumerable.Range(0, 50).Select(i => $"item{i}");
        await Assert.That(ListonicTelemetry.Capped(many).Length).IsEqualTo(ListonicTelemetry.MaxListedValues);
    }

    [Test]
    public async Task GetItems_TagsListAndCount_OutcomeOk()
    {
        var api = Substitute.For<IListonicApi>();
        api.GetItemsAsync("7", Arg.Any<CancellationToken>()).Returns([Fake.Item("a", "mleko"), Fake.Item("b", "chleb")]);
        var tools = new ListonicTools(api, NullLogger<ListonicTools>.Instance);

        using var span = TestSource.StartActivity("tools/call get_items")!;
        await tools.GetItemsAsync("7", "Zakupy");

        await Assert.That(span.GetTagItem(ListonicTelemetry.Tags.ListId)).IsEqualTo("7");
        await Assert.That(span.GetTagItem(ListonicTelemetry.Tags.ListName)).IsEqualTo("Zakupy");
        await Assert.That(span.GetTagItem(ListonicTelemetry.Tags.ItemsCount)).IsEqualTo(2);
        await Assert.That(span.GetTagItem(ListonicTelemetry.Tags.Outcome)).IsEqualTo("ok");
        await Assert.That(span.Status).IsEqualTo(ActivityStatusCode.Unset);
    }

    [Test]
    public async Task GetLists_TagsCount()
    {
        var api = Substitute.For<IListonicApi>();
        api.GetListsAsync(Arg.Any<GetListsRequest>(), Arg.Any<CancellationToken>()).Returns([Fake.List("1", "A")]);

        using var span = TestSource.StartActivity("tools/call get_lists")!;
        await new ListonicTools(api, NullLogger<ListonicTools>.Instance).GetListsAsync();

        await Assert.That(span.GetTagItem(ListonicTelemetry.Tags.ListsCount)).IsEqualTo(1);
    }

    [Test]
    public async Task Fail_SetsErrorStatus()
    {
        using var span = TestSource.StartActivity("tools/call x")!;
        ListonicTelemetry.Fail("list_not_found", "404");

        await Assert.That(span.GetTagItem(ListonicTelemetry.Tags.Outcome)).IsEqualTo("list_not_found");
        await Assert.That(span.Status).IsEqualTo(ActivityStatusCode.Error);
    }

    [Test]
    public async Task NoCurrentSpan_HelpersAreNoOps()
    {
        // Telemetry off: no current span — helpers must not throw.
        Activity.Current = null;
        ListonicTelemetry.Tag(ListonicTelemetry.Tags.ListId, "X");
        ListonicTelemetry.Fail("x", "y");
        ListonicTelemetry.Event("e", ("k", 1));
        await Assert.That(Activity.Current).IsNull();
    }

    [Test]
    [Arguments("/.well-known/oauth-authorization-server", true)]
    [Arguments("/favicon.ico", true)]
    [Arguments("/apple-touch-icon.png", true)]
    [Arguments("/icon-192.png", true)]
    [Arguments("/mcp", false)]
    [Arguments("/token", false)]
    [Arguments("/authorize", false)]
    public async Task Noise_Filter(string path, bool isNoise) =>
        await Assert.That(TelemetryNoise.IsNoise(new PathString(path))).IsEqualTo(isNoise);

    [Test]
    public async Task TelemetryValidator_RequiresAbsoluteHttpEndpoint_WhenEnabled()
    {
        var v = new TelemetryOptionsValidator();
        await Assert.That(v.Validate(null, new TelemetryOptions { Enabled = true, OtlpEndpoint = "alloy:4317" }).Failed).IsTrue();
        await Assert.That(v.Validate(null, new TelemetryOptions { Enabled = true, OtlpEndpoint = "http://alloy:4317" }).Succeeded).IsTrue();
        await Assert.That(v.Validate(null, new TelemetryOptions { Enabled = false, OtlpEndpoint = "junk" }).Succeeded).IsTrue();
    }
}
