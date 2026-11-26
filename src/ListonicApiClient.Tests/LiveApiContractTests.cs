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
[NotInParallel(LiveApi.Serial)]
public class LiveApiContractTests
{
    private const string UnknownId = LiveApi.UnknownId;
    private readonly IListonicApi _api = ApiClientFactory.Api;
    private static IHttpClientFactory HttpClients => ApiClientFactory.Services.GetRequiredService<IHttpClientFactory>();

    // Before: removes "[test] " lists left behind by a crashed previous run. After: safety net for the
    // per-test finally blocks.
    [Before(HookType.Class)]
    public static Task SweepBefore() => LiveApi.SweepTestListsAsync();

    [After(HookType.Class)]
    public static Task SweepAfter() => LiveApi.SweepTestListsAsync();

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

    [Test]
    public async Task InvalidBearerToken_Is401()
    {
        // ListonicAuthHandler re-logs in only on 401 — an expired/invalid token must produce exactly that.
        using var http = HttpClients.CreateClient(ServiceCollectionExtensions.LoginClientName);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{ListonicClientOptions.BaseUrl}/lists");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "not-a-real-token");

        using var response = await http.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
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

    [Test]
    public async Task RenameList_UnknownId_IsSilent200()
    {
        // Why rename_list checks GET /lists/{id} first instead of trusting the PATCH.
        await _api.RenameListAsync(UnknownId, new RenameListRequest { Id = UnknownId, Name = "x" }); // must not throw
    }

    [Test]
    public async Task DeleteList_UnknownId_Is404()
    {
        // delete_list relies on this 404 (mapped to "not found") rather than claiming "moved to trash".
        var ex = await Assert.ThrowsAsync<ApiException>(() => _api.DeleteListAsync(UnknownId));
        await Assert.That(ex!.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task DeletedList_IsGoneFromApi_EvenWithArchive()
    {
        // delete_list output says "moved to trash"; through the API a deleted list is simply gone.
        var list = await _api.CreateListAsync(new CreateListRequest { Name = "[test] contract delete" });
        await _api.DeleteListAsync(list.Id);

        var get = await Assert.ThrowsAsync<ApiException>(() => _api.GetListAsync(list.Id));
        await Assert.That(get!.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        var archived = await _api.GetListsAsync(new GetListsRequest { IncludeShares = true, Archive = true });
        await Assert.That(archived.Any(l => l.Id == list.Id)).IsFalse();
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
    public async Task NewList_IsPrivate_OneShare_Owned_WithName()
    {
        // get_lists labels a list "shared" only when Shares.Count > 1 (the owner counts as one share);
        // create_list formats from the create response's Id + Name.
        var list = await _api.CreateListAsync(new CreateListRequest { Name = "[test] contract private" });
        try
        {
            await Assert.That(list.Id).IsNotEmpty();
            await Assert.That(list.Name).IsEqualTo("[test] contract private");
            var listed = (await _api.GetListsAsync(new GetListsRequest { IncludeShares = true })).Single(l => l.Id == list.Id);
            await Assert.That(listed.Shares.Count).IsEqualTo(1);
            await Assert.That(listed.IsOwner).IsEqualTo(1);
            await Assert.That(listed.ItemsCount).IsEqualTo(0);
        }
        finally
        {
            await _api.DeleteListAsync(list.Id);
        }
    }

    [Test]
    public async Task ItemQuantity_NoAmountIsEmpty_ClearingGivesZero_UnicodeNamesRoundTrip()
    {
        // FormatItem hides the quantity when Amount is "" or "0"; edit_items documents
        // "amount 0 + unit \"\" clears the quantity"; item names are in the user's language.
        var list = await _api.CreateListAsync(new CreateListRequest { Name = "[test] contract quantity" });
        try
        {
            var plain = await _api.AddItemAsync(list.Id, new AddItemRequest { Name = "żółć ąęśń" });
            await Assert.That(plain.Amount).IsEqualTo("");
            await Assert.That(plain.Name).IsEqualTo("żółć ąęśń");

            var milk = await _api.AddItemAsync(list.Id, new AddItemRequest { Name = "mleko", Quantity = 2, Unit = "l" });
            await _api.UpdateItemAsync(list.Id, milk.Id, new UpdateItemRequest { Amount = 0, Unit = "" });
            var cleared = (await _api.GetItemsAsync(list.Id)).Single(i => i.Id == milk.Id);
            await Assert.That(cleared.Amount is "" or "0").IsTrue();
            await Assert.That(cleared.Unit).IsEqualTo("");
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
