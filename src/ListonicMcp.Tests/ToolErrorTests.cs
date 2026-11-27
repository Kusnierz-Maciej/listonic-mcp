using System.Net;
using ListonicApiClient;
using ListonicMcp.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Refit;

namespace ListonicMcp.Tests;

public class ToolErrorTests
{
    private static async Task<ApiException> Api(HttpStatusCode status) =>
        await ApiException.Create(
            new HttpRequestMessage(HttpMethod.Get, "https://api.listonic.com/api/lists/1/items"),
            HttpMethod.Get,
            new HttpResponseMessage(status) { Content = new StringContent("{}") },
            new RefitSettings());

    [Test]
    [Arguments(HttpStatusCode.NotFound, "not_found")]
    [Arguments(HttpStatusCode.Forbidden, "forbidden")]
    [Arguments(HttpStatusCode.Unauthorized, "credentials_rejected")]
    [Arguments(HttpStatusCode.TooManyRequests, "rate_limited")]
    [Arguments(HttpStatusCode.BadGateway, "unavailable")]
    [Arguments(HttpStatusCode.BadRequest, "rejected_request")]
    public async Task ApiStatus_MapsToOutcome(HttpStatusCode status, string outcome) =>
        await Assert.That(ToolErrors.Map(await Api(status))!.Outcome).IsEqualTo(outcome);

    [Test]
    public async Task RejectedCredentials_FoundInsideWrapper()
    {
        // Refit wraps handler exceptions (e.g. ApiRequestException → inner).
        var wrapped = new InvalidOperationException("outer", new ListonicCredentialsRejectedException("HTTP 400"));
        await Assert.That(ToolErrors.Map(wrapped)!.Outcome).IsEqualTo("credentials_rejected");
    }

    [Test]
    public async Task Transport_IsUnavailable_Cancellation_And_Unknown_AreNotMapped()
    {
        await Assert.That(ToolErrors.Map(new HttpRequestException("dns"))!.Outcome).IsEqualTo("unavailable");
        await Assert.That(ToolErrors.Map(new OperationCanceledException())).IsNull();
        await Assert.That(ToolErrors.Map(new InvalidOperationException("bug"))).IsNull();
    }

    [Test]
    public async Task Tool_404_ThrowsMcpException_WithPlainMessage()
    {
        var api = Substitute.For<IListonicApi>();
        api.GetItemsAsync("1", Arg.Any<CancellationToken>()).ThrowsAsync(await Api(HttpStatusCode.NotFound));
        var tools = new ListonicTools(api, NullLogger<ListonicTools>.Instance);

        var ex = await Assert.ThrowsAsync<McpException>(() => tools.GetItemsAsync("1", "Zakupy"));

        await Assert.That(ex!.Message).Contains("couldn't find that list or item");
    }

    [Test]
    public async Task Tool_RejectedCredentials_AsksToReconnect()
    {
        var api = Substitute.For<IListonicApi>();
        api.DeleteListAsync("1", Arg.Any<CancellationToken>()).ThrowsAsync(new ListonicCredentialsRejectedException());
        var tools = new ListonicTools(api, NullLogger<ListonicTools>.Instance);

        var ex = await Assert.ThrowsAsync<McpException>(() => tools.DeleteListAsync("1", "Zakupy"));

        await Assert.That(ex!.Message).IsEqualTo(ToolErrors.CredentialsRejected);
    }
}
