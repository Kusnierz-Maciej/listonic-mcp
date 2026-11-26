using System.Diagnostics;
using ListonicApiClient;
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

    static TelemetryTests()
    {
        ActivitySource.AddActivityListener(new ActivityListener
        {
            ShouldListenTo = s => s.Name == TestSource.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        });
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
