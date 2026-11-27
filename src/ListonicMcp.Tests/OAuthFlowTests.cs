using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ListonicApiClient;
using ListonicApiClient.Models.Lists;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace ListonicMcp.Tests;

/// <summary>
/// The whole OAuth 2.1 flow against the real HTTP host running in memory (no process, no ports, no account):
/// metadata → dynamic registration → sign-in form → code + PKCE → token → Bearer /mcp tool call → refresh
/// rotation → key revocation. The Listonic login validator and API are mocked; the real login is pinned by the
/// live Login_* contract tests.
/// </summary>
public class OAuthFlowTests
{
    private const string Secret = "test-token-secret-at-least-32-chars-long";
    private const string Email = "user@example.com";
    private const string Password = "correct-password";
    private const string Redirect = "http://localhost:9/callback";

    private static WebApplicationFactory<Program> Server(string apiKey = "key-a", bool requireKey = true,
        string? accessLifetime = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(web =>
        {
            web.UseSetting("Security:AllowedApiKeys:0", apiKey);
            web.UseSetting("Security:TokenSecret", Secret);
            web.UseSetting("Security:RequireMcpApiKey", requireKey.ToString());
            if (accessLifetime is not null) web.UseSetting("OAuth:AccessTokenLifetime", accessLifetime);
            web.ConfigureTestServices(services =>
            {
                var validator = Substitute.For<IListonicLoginValidator>();
                validator.ValidateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                    .Returns(ci => ci.ArgAt<string>(0) == Email && ci.ArgAt<string>(1) == Password
                        ? LoginOutcome.Success("listonic-token")
                        : LoginOutcome.Bad("HTTP 400"));
                services.AddTransient(_ => validator);

                var api = Substitute.For<IListonicApi>();
                api.GetListsAsync(Arg.Any<GetListsRequest>(), Arg.Any<CancellationToken>())
                    .Returns([Fake.List("1", "Zakupy", items: 3, checkedItems: 1)]);
                services.AddTransient(_ => api);
            });
        });

    private static HttpClient Client(WebApplicationFactory<Program> server) =>
        server.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static (string Verifier, string Challenge) NewPkce()
    {
        var verifier = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var challenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return (verifier, challenge);
    }

    private static Task<HttpResponseMessage> PostForm(HttpClient http, string path, Dictionary<string, string> form) =>
        http.PostAsync(path, new FormUrlEncodedContent(form));

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    /// <summary>Signs in through the form and returns the authorization code (asserting the redirect).</summary>
    private static async Task<string> SignInAsync(HttpClient http, string challenge, string? apiKey = "key-a")
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = "mcp-test", ["redirect_uri"] = Redirect, ["state"] = "xyz", ["code_challenge"] = challenge,
            ["listonic_email"] = Email, ["listonic_password"] = Password,
        };
        if (apiKey is not null) form["api_key"] = apiKey;

        var response = await PostForm(http, "/authorize", form);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        var location = response.Headers.Location!.ToString();
        await Assert.That(location).StartsWith(Redirect + "?code=");
        await Assert.That(location).EndsWith("&state=xyz");
        return Uri.UnescapeDataString(Regex.Match(location, "code=([^&]+)").Groups[1].Value);
    }

    private static Task<HttpResponseMessage> ExchangeAsync(HttpClient http, string code, string verifier) =>
        PostForm(http, "/token", new()
        {
            ["grant_type"] = "authorization_code", ["code"] = code, ["code_verifier"] = verifier, ["redirect_uri"] = Redirect,
        });

    private static Task<HttpResponseMessage> RefreshAsync(HttpClient http, string refreshToken) =>
        PostForm(http, "/token", new() { ["grant_type"] = "refresh_token", ["refresh_token"] = refreshToken });

    /// <summary>initialize + tools/call over Streamable HTTP with a Bearer token; returns the tool's text.</summary>
    private static async Task<string> CallGetListsAsync(HttpClient http, string accessToken)
    {
        async Task<HttpResponseMessage> Rpc(string json)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Accept.ParseAdd("application/json");
            request.Headers.Accept.ParseAdd("text/event-stream");
            return await http.SendAsync(request);
        }

        var init = await Rpc("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}""");
        await Assert.That(init.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var call = await Rpc("""{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"get_lists","arguments":{}}}""");
        await Assert.That(call.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var body = await call.Content.ReadAsStringAsync();
        var data = body.Split('\n').Where(l => l.StartsWith("data:")).Select(l => l[5..].Trim()).LastOrDefault() ?? body;
        var result = JsonDocument.Parse(data).RootElement.GetProperty("result");
        await Assert.That(result.TryGetProperty("isError", out var isError) && isError.GetBoolean()).IsFalse();
        return result.GetProperty("content")[0].GetProperty("text").GetString()!;
    }

    [Test]
    public async Task FullFlow_SignIn_Token_ToolCall_Refresh()
    {
        await using var server = Server();
        var http = Client(server);

        // Discovery + dynamic client registration.
        var meta = await Json(await http.GetAsync("/.well-known/oauth-authorization-server"));
        await Assert.That(meta.GetProperty("code_challenge_methods_supported")[0].GetString()).IsEqualTo("S256");
        var registration = await http.PostAsync("/register",
            new StringContent($$"""{"redirect_uris":["{{Redirect}}"]}""", Encoding.UTF8, "application/json"));
        await Assert.That(registration.StatusCode).IsEqualTo(HttpStatusCode.Created);

        // A bare /mcp call is challenged towards the metadata.
        var bare = await http.PostAsync("/mcp", new StringContent("{}", Encoding.UTF8, "application/json"));
        await Assert.That(bare.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(bare.Headers.WwwAuthenticate.ToString()).Contains("resource_metadata=");

        // Sign-in form renders; wrong password is refused on the form.
        var (verifier, challenge) = NewPkce();
        var form = await http.GetAsync($"/authorize?response_type=code&client_id=mcp-test&redirect_uri={Uri.EscapeDataString(Redirect)}&state=xyz&code_challenge={challenge}&code_challenge_method=S256");
        await Assert.That(await form.Content.ReadAsStringAsync()).Contains("Connect Listonic (unofficial)");
        var wrong = await PostForm(http, "/authorize", new()
        {
            ["redirect_uri"] = Redirect, ["code_challenge"] = challenge, ["api_key"] = "key-a",
            ["listonic_email"] = Email, ["listonic_password"] = "nope",
        });
        await Assert.That(wrong.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);

        // Correct sign-in → code; a wrong PKCE verifier is rejected; the right one gets tokens.
        var code = await SignInAsync(http, challenge);
        await Assert.That((await ExchangeAsync(http, code, verifier + "x")).StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var tokens = await Json(await ExchangeAsync(http, code, verifier));
        var access = tokens.GetProperty("access_token").GetString()!;
        var refresh = tokens.GetProperty("refresh_token").GetString()!;
        await Assert.That(tokens.GetProperty("expires_in").GetInt32()).IsEqualTo(3600);

        // The access token works on /mcp end to end.
        await Assert.That(await CallGetListsAsync(http, access)).IsEqualTo("[1] Zakupy (private) - 2 unchecked");

        // Refresh rotates; an access token can't be used as a refresh token.
        var refreshed = await RefreshAsync(http, refresh);
        await Assert.That(refreshed.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var newAccess = (await Json(refreshed)).GetProperty("access_token").GetString()!;
        await Assert.That(newAccess).IsNotEqualTo(access);
        await Assert.That((await RefreshAsync(http, access)).StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(await CallGetListsAsync(http, newAccess)).Contains("Zakupy");
    }

    private const string WrongPassword = "wrong <&\" pass";

    /// <summary>The page script's background submit: same form fields, Accept: application/json.</summary>
    private static Task<HttpResponseMessage> PostFormAsJson(HttpClient http, Dictionary<string, string> form)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/authorize") { Content = new FormUrlEncodedContent(form) };
        request.Headers.Accept.ParseAdd("application/json");
        return http.SendAsync(request);
    }

    private static Dictionary<string, string> SignInForm(string challenge, string password) => new()
    {
        ["client_id"] = "mcp-test", ["redirect_uri"] = Redirect, ["state"] = "xyz", ["code_challenge"] = challenge,
        ["api_key"] = "key-a", ["listonic_email"] = Email, ["listonic_password"] = password,
    };

    [Test]
    public async Task BackgroundSignIn_Failure_IsJsonError_AndEchoesNothing()
    {
        await using var server = Server();
        var (_, challenge) = NewPkce();

        var wrong = await PostFormAsJson(Client(server), SignInForm(challenge, WrongPassword));
        var body = await wrong.Content.ReadAsStringAsync();

        await Assert.That(wrong.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(wrong.Content.Headers.ContentType!.MediaType).IsEqualTo("application/json");
        var json = JsonDocument.Parse(body).RootElement;
        await Assert.That(json.GetProperty("error").GetString()!).Contains("Listonic rejected these credentials");
        await Assert.That(json.GetProperty("field").GetString()).IsEqualTo("credentials");
        await Assert.That(body).DoesNotContain("wrong");
        await Assert.That(body).DoesNotContain("key-a");
        await Assert.That(body).DoesNotContain(Email);
    }

    [Test]
    public async Task BackgroundSignIn_WrongApiKey_PointsAtTheApiKeyField()
    {
        await using var server = Server();
        var (_, challenge) = NewPkce();
        var form = SignInForm(challenge, Password);
        form["api_key"] = "not-a-key";

        var response = await PostFormAsJson(Client(server), form);
        var json = await Json(response);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(json.GetProperty("field").GetString()).IsEqualTo("api_key");
        await Assert.That(json.GetProperty("error").GetString()!).Contains("API key");
    }

    [Test]
    public async Task BackgroundSignIn_Success_ReturnsRedirectUrl_WithWorkingCode()
    {
        await using var server = Server();
        var http = Client(server);
        var (verifier, challenge) = NewPkce();

        var ok = await PostFormAsJson(http, SignInForm(challenge, Password));
        await Assert.That(ok.StatusCode).IsEqualTo(HttpStatusCode.OK);   // not a 302: fetch would follow it itself
        var redirect = (await Json(ok)).GetProperty("redirect").GetString()!;

        await Assert.That(redirect).StartsWith(Redirect + "?code=");
        await Assert.That(redirect).EndsWith("&state=xyz");
        var code = Uri.UnescapeDataString(Regex.Match(redirect, "code=([^&]+)").Groups[1].Value);
        await Assert.That((await ExchangeAsync(http, code, verifier)).StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task NoScriptSignIn_Failure_ReRendersForm_KeepingOnlyTheEmail_NotCached()
    {
        await using var server = Server();
        var (_, challenge) = NewPkce();

        var wrong = await PostForm(Client(server), "/authorize", SignInForm(challenge, WrongPassword));
        var html = await wrong.Content.ReadAsStringAsync();

        await Assert.That(wrong.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(html).Contains("Listonic rejected these credentials");
        await Assert.That(html).Contains($"value=\"{Email}\"");
        await Assert.That(html).DoesNotContain("wrong &lt;&amp;&quot; pass");
        await Assert.That(html).DoesNotContain("value=\"key-a\"");
        await Assert.That(wrong.Headers.CacheControl?.NoStore).IsTrue();
    }

    [Test]
    public async Task RemovingTheApiKey_RevokesItsTokens()
    {
        string access, refresh;
        await using (var before = Server(apiKey: "key-a"))
        {
            var http = Client(before);
            var (verifier, challenge) = NewPkce();
            var tokens = await Json(await ExchangeAsync(http, await SignInAsync(http, challenge), verifier));
            access = tokens.GetProperty("access_token").GetString()!;
            refresh = tokens.GetProperty("refresh_token").GetString()!;
        }

        // "Restart" with key-a removed (same token secret): both tokens are dead.
        await using var after = Server(apiKey: "key-b");
        var client = Client(after);
        await Assert.That((await RefreshAsync(client, refresh)).StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        await Assert.That((await client.SendAsync(request)).StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task WithoutApiKeyRequirement_FormHasNoKeyField_AndSignInWorks()
    {
        await using var server = Server(requireKey: false);
        var http = Client(server);
        var (verifier, challenge) = NewPkce();

        var form = await http.GetAsync($"/authorize?response_type=code&client_id=x&redirect_uri={Uri.EscapeDataString(Redirect)}&code_challenge={challenge}&code_challenge_method=S256");
        await Assert.That(await form.Content.ReadAsStringAsync()).DoesNotContain("name=\"api_key\"");

        var tokens = await ExchangeAsync(http, await SignInAsync(http, challenge, apiKey: null), verifier);
        await Assert.That(tokens.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task AccessTokenLifetime_Setting_IsUsed()
    {
        await using var server = Server(accessLifetime: "00:00:30");
        var http = Client(server);
        var (verifier, challenge) = NewPkce();

        var tokens = await Json(await ExchangeAsync(http, await SignInAsync(http, challenge), verifier));

        await Assert.That(tokens.GetProperty("expires_in").GetInt32()).IsEqualTo(30);
    }
}
