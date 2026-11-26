using System.Net;
using System.Text.Json;
using ListonicApiClient.Models.Items;
using ListonicApiClient.Models.Lists;
using ListonicApiClient.Models.Login;
using Microsoft.Extensions.DependencyInjection;
using Refit;

namespace ListonicApiClient.Tests;

/// <summary>
/// Pins the Listonic API behaviours this server's logic DEPENDS ON (docs/API-NOTES.md). If one of these
/// fails in the nightly contract check, the matching code path needs revisiting — each test names it.
/// Read-only except for one self-created "[test] " list, deleted in finally.
/// </summary>
[Category("LiveApi")]
public class LiveApiContractTests
{
    private const string UnknownId = "999999999";
    private readonly IListonicApi _api = ApiClientFactory.Api;
    private static IHttpClientFactory HttpClients => ApiClientFactory.Services.GetRequiredService<IHttpClientFactory>();

    // ─── Login (ListonicLogin.Classify, OAuth sign-in validation) ─────────────────────────────────

    [Test]
    public async Task Login_Valid_IsOk_WithDayLongToken()
    {
        var outcome = await ListonicLogin.TryLoginAsync(
            HttpClients, ApiClientFactory.Options.Email, ApiClientFactory.Options.Password, null, CancellationToken.None);
        await Assert.That(outcome.Kind).IsEqualTo(LoginOutcomeKind.Ok);

        // Token cache has no expiry; it relies on re-login after a 401. A much shorter TTL would mean frequent re-logins.
        var raw = await _api.LoginAsync(ApiClientFactory.CreateLoginRequest());
        await Assert.That(raw.TokenType).IsEqualTo("Bearer");
        await Assert.That(raw.ExpiresIn).IsGreaterThanOrEqualTo(3600);
    }

    [Test]
    public async Task Login_WrongPassword_Is400UnauthorizedToken_ClassifiedAsBadCredentials()
    {
        // Raw shape: HTTP 400 {"Message":"Unauthorized token. Token is invalid"}.
        var ex = await Assert.ThrowsAsync<ApiException>(() => _api.LoginAsync(new LoginRequest
        {
            Username = ApiClientFactory.Options.Email,
            Password = ApiClientFactory.Options.Password + "-wrong",
        }));
        await Assert.That(ex!.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(ex.Content!).Contains("Unauthorized token");

        // And the classifier the auth handler + OAuth form use must call it bad credentials, not an API error.
        var outcome = await ListonicLogin.TryLoginAsync(
            HttpClients, ApiClientFactory.Options.Email, ApiClientFactory.Options.Password + "-wrong", null, CancellationToken.None);
        await Assert.That(outcome.Kind).IsEqualTo(LoginOutcomeKind.BadCredentials);
    }

    [Test]
    public async Task Login_UnknownEmail_ClassifiedAsBadCredentials()
    {
        var outcome = await ListonicLogin.TryLoginAsync(
            HttpClients, $"nobody-{Guid.NewGuid():N}@example.invalid", "x", null, CancellationToken.None);
        await Assert.That(outcome.Kind).IsEqualTo(LoginOutcomeKind.BadCredentials);
    }

    // ─── Unknown ids (ToolErrors 404 mapping, get_items / delete_items workarounds) ───────────────

    [Test]
    public async Task GetList_UnknownId_Is404_ProblemDetails()
    {
        // get_items relies on this 404 to tell a wrong list id from an empty list.
        var ex = await Assert.ThrowsAsync<ApiException>(() => _api.GetListAsync(UnknownId));
        await Assert.That(ex!.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(ex.Content!).Contains("was not found");
    }

    [Test]
    public async Task GetItems_UnknownList_Is200Empty()
    {
        // Why get_items re-checks GET /lists/{id} on an empty result. If this starts returning 404,
        // the re-check becomes redundant (harmless) — update docs/API-NOTES.md.
        var items = await _api.GetItemsAsync(UnknownId);
        await Assert.That(items).IsEmpty();
    }

    [Test]
    public async Task UnknownItem_PatchIs404_DeleteIsSilent200()
    {
        var list = await _api.CreateListAsync(new CreateListRequest { Name = "[test] contract unknown item" });
        try
        {
            var patch = await Assert.ThrowsAsync<ApiException>(() =>
                _api.UpdateItemAsync(list.Id, UnknownId, new UpdateItemRequest { IsChecked = true }));
            await Assert.That(patch!.StatusCode).IsEqualTo(HttpStatusCode.NotFound);

            // Why delete_items checks ids against the list first instead of trusting DELETE.
            await _api.DeleteItemAsync(list.Id, UnknownId); // must not throw
        }
        finally
        {
            await _api.DeleteListAsync(list.Id);
        }
    }

    // ─── Payload shapes ───────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task AddItem_ReturnsFullItem_AmountAsString_Unchecked()
    {
        // add_items formats from a re-read, but the id from the add response must be populated.
        var list = await _api.CreateListAsync(new CreateListRequest { Name = "[test] contract add" });
        try
        {
            var item = await _api.AddItemAsync(list.Id, new AddItemRequest { Name = "mleko", Quantity = 0.5m, Unit = "l" });
            await Assert.That(item.Id).IsNotEmpty();
            await Assert.That(item.Checked).IsFalse();
            await Assert.That(item.Amount).IsEqualTo("0.5");
            await Assert.That(item.Unit).IsEqualTo("l");
        }
        finally
        {
            await _api.DeleteListAsync(list.Id);
        }
    }

    [Test]
    public async Task Categories_AreFlatList_WithIdAndName()
    {
        // Shape documented for the planned categories feature.
        var raw = (JsonElement)await _api.GetCategoriesAsync();
        await Assert.That(raw.ValueKind).IsEqualTo(JsonValueKind.Array);
        await Assert.That(raw.GetArrayLength()).IsGreaterThan(0);
        var first = raw[0];
        await Assert.That(first.TryGetProperty("Id", out _)).IsTrue();
        await Assert.That(first.TryGetProperty("Name", out _)).IsTrue();
    }
}
