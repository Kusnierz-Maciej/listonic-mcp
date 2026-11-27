using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using ListonicApiClient;
using ListonicApiClient.Models.Lists;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace ListonicMcp.Tests;

/// <summary>
/// Client trace-context detection against the in-memory HTTP host: a traceparent in params._meta (SEP-414) or the HTTP
/// header is reported on the tools/call span and continues the client's trace; client name/version and _meta key names
/// are recorded.
/// </summary>
public class ClientContextFilterTests
{
    private static readonly ConcurrentBag<Activity> Spans = [];

    static ClientContextFilterTests()
    {
        ActivitySource.AddActivityListener(new ActivityListener
        {
            ShouldListenTo = s => s.Name == "Experimental.ModelContextProtocol",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a => Spans.Add(a),
        });
    }

    private static WebApplicationFactory<Program> Server() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(web =>
        {
            web.UseSetting("Security:AllowedApiKeys:0", "k");
            web.UseSetting("Security:TokenSecret", "test-token-secret-at-least-32-chars-long");
            web.ConfigureTestServices(services =>
            {
                var api = Substitute.For<IListonicApi>();
                api.GetListsAsync(Arg.Any<GetListsRequest>(), Arg.Any<CancellationToken>()).Returns([Fake.List("1", "Zakupy")]);
                services.AddTransient(_ => api);
            });
        });

    /// <summary>Calls get_lists with the given _meta JSON (and optional HTTP traceparent); returns the tools/call span.</summary>
    /// <remarks><paramref name="metaJson"/> must contain a unique <c>"test.marker/&lt;guid&gt;"</c> key; it identifies the span.</remarks>
    private static async Task<Activity> CallAsync(HttpClient http, string marker, string metaJson, string? httpTraceparent = null)
    {
        var body = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"get_lists\",\"arguments\":{},\"_meta\":"
                   + metaJson + "}}";
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Api-Key", "k");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("a@b.c:pw")));
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        if (httpTraceparent is not null) request.Headers.Add("traceparent", httpTraceparent);

        var response = await http.SendAsync(request);
        await Assert.That((int)response.StatusCode).IsEqualTo(200);
        await response.Content.ReadAsStringAsync();

        // The span with our filter's tags for this request (spans are collected process-wide).
        for (var i = 0; i < 50; i++)
        {
            var span = Spans.FirstOrDefault(a => a.GetTagItem(ListonicTelemetry.Tags.ClientMetaKeys) is string[] keys
                && keys.Contains("test.marker/" + marker));
            if (span is not null) return span;
            await Task.Delay(20);
        }
        throw new InvalidOperationException("tools/call span not recorded");
    }

    [Test]
    public async Task TraceparentInMeta_ContinuesClientTrace_AndIsReported()
    {
        await using var server = Server();
        var http = server.CreateClient();
        const string traceId = "4bf92f3577b34da6a3ce929d0e0e4736";
        var marker = Guid.NewGuid().ToString("N");

        var span = await CallAsync(http, marker,
            $$"""{"traceparent":"00-{{traceId}}-00f067aa0ba902b7-01","io.modelcontextprotocol/clientInfo":{"name":"test-client","version":"9.9"},"test.marker/{{marker}}":true}""");

        await Assert.That(span.TraceId.ToString()).IsEqualTo(traceId);
        await Assert.That(span.GetTagItem(ListonicTelemetry.Tags.ClientTraceContext)).IsEqualTo("meta");
        await Assert.That(span.GetTagItem(ListonicTelemetry.Tags.ClientName)).IsEqualTo("test-client");
        await Assert.That(span.GetTagItem(ListonicTelemetry.Tags.ClientVersion)).IsEqualTo("9.9");
        await Assert.That((string[])span.GetTagItem(ListonicTelemetry.Tags.ClientMetaKeys)!).Contains("traceparent");
    }

    [Test]
    public async Task TraceparentHeaderOnly_IsReportedAsHttp()
    {
        await using var server = Server();
        var marker = Guid.NewGuid().ToString("N");

        var span = await CallAsync(server.CreateClient(), marker, $$"""{"test.marker/{{marker}}":true}""",
            httpTraceparent: "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01");

        await Assert.That(span.GetTagItem(ListonicTelemetry.Tags.ClientTraceContext)).IsEqualTo("http");
    }

    [Test]
    public async Task NoTraceContext_IsReportedAsNone()
    {
        await using var server = Server();
        var marker = Guid.NewGuid().ToString("N");

        var span = await CallAsync(server.CreateClient(), marker, $$"""{"test.marker/{{marker}}":true}""");

        await Assert.That(span.GetTagItem(ListonicTelemetry.Tags.ClientTraceContext)).IsEqualTo("none");
    }
}
