using System.Net;
using System.Text;
using ListonicApiClient;
using Microsoft.AspNetCore.Mvc;

namespace ListonicMcp.Security.OAuth;

/// <summary>
/// Minimal built-in OAuth 2.1 authorization server: discovery, dynamic client registration, an HTML login
/// (API key + Listonic email + password), and the token endpoint (authorization_code + refresh_token). Stateless
/// — the authorization code and tokens are self-contained encrypted blobs (<see cref="CredentialTokenService"/>);
/// there is no client store or session store. Access is gated by the <see cref="ApiKeyAllowlist"/>; Listonic
/// credentials are validated at login via <see cref="IListonicLoginValidator"/>.
/// </summary>
public static class OAuthEndpoints
{
    private static readonly TimeSpan CodeTtl = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan AccessTtl = TimeSpan.FromHours(1);
    private static readonly TimeSpan RefreshTtl = TimeSpan.FromDays(365);

    public static void MapOAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/.well-known/oauth-protected-resource", ProtectedResourceMetadataDoc);
        app.MapGet("/.well-known/oauth-authorization-server", AuthorizationServerMetadata);
        app.MapPost("/register", Register);
        app.MapGet("/authorize", AuthorizeForm);
        app.MapPost("/authorize", AuthorizeSubmit);
        app.MapPost("/token", Token);
    }

    // The protected-resource metadata (RFC 9728) clients fetch after a 401. Since this app is its own
    // authorization server, it points back at this same issuer.
    private static IResult ProtectedResourceMetadataDoc(HttpContext ctx)
    {
        var issuer = Issuer(ctx);
        return Results.Json(new Dictionary<string, object>
        {
            ["resource"] = issuer,
            ["authorization_servers"] = new[] { issuer },
            ["bearer_methods_supported"] = new[] { "header" },
        });
    }

    private static IResult AuthorizationServerMetadata(HttpContext ctx)
    {
        var issuer = Issuer(ctx);
        return Results.Json(new Dictionary<string, object>
        {
            ["issuer"] = issuer,
            ["authorization_endpoint"] = $"{issuer}/authorize",
            ["token_endpoint"] = $"{issuer}/token",
            ["registration_endpoint"] = $"{issuer}/register",
            ["response_types_supported"] = new[] { "code" },
            ["grant_types_supported"] = new[] { "authorization_code", "refresh_token" },
            ["code_challenge_methods_supported"] = new[] { "S256" },
            ["token_endpoint_auth_methods_supported"] = new[] { "none" },
        });
    }

    // Dynamic client registration (RFC 7591). Stateless: we don't persist clients. We echo a generated
    // client_id and the submitted redirect_uris; PKCE + exact redirect_uri matching secure the flow.
    private static IResult Register([FromBody] ClientRegistrationRequest? body, ILogger<Marker> logger)
    {
        var redirectUris = body?.RedirectUris ?? [];
        logger.LogInformation("OAuth client registration for {Count} redirect URI(s).", redirectUris.Length);
        var clientId = "mcp-" + Guid.NewGuid().ToString("N");
        return Results.Json(new Dictionary<string, object?>
        {
            ["client_id"] = clientId,
            ["redirect_uris"] = redirectUris,
            ["token_endpoint_auth_method"] = "none",
            ["grant_types"] = new[] { "authorization_code", "refresh_token" },
            ["response_types"] = new[] { "code" },
        }, statusCode: StatusCodes.Status201Created);
    }

    private static IResult AuthorizeForm(
        HttpContext ctx,
        ApiKeyAllowlist allowlist,
        [FromQuery(Name = "client_id")] string? clientId,
        [FromQuery(Name = "redirect_uri")] string? redirectUri,
        [FromQuery] string? state,
        [FromQuery(Name = "code_challenge")] string? codeChallenge,
        [FromQuery(Name = "code_challenge_method")] string? codeChallengeMethod,
        [FromQuery(Name = "response_type")] string? responseType)
    {
        if (!string.Equals(responseType, "code", StringComparison.Ordinal))
            return Results.BadRequest("Unsupported response_type; only 'code' is supported.");
        if (!string.Equals(codeChallengeMethod, "S256", StringComparison.Ordinal))
            return Results.BadRequest("PKCE with code_challenge_method=S256 is required.");
        if (string.IsNullOrWhiteSpace(redirectUri) || string.IsNullOrWhiteSpace(codeChallenge))
            return Results.BadRequest("redirect_uri and code_challenge are required.");

        return Html(RenderForm(clientId, redirectUri, state, codeChallenge, allowlist.RequireApiKey, error: null));
    }

    private static async Task<IResult> AuthorizeSubmit(
        HttpContext ctx,
        ApiKeyAllowlist allowlist,
        IListonicLoginValidator loginValidator,
        CredentialTokenService tokens,
        ILogger<Marker> logger,
        CancellationToken ct)
    {
        var form = await ctx.Request.ReadFormAsync(ct);
        string? F(string k) => form[k].FirstOrDefault();

        var redirectUri = F("redirect_uri");
        var state = F("state");
        var codeChallenge = F("code_challenge");
        var clientId = F("client_id");
        var apiKey = F("api_key");
        var email = F("listonic_email");
        var password = F("listonic_password");

        if (string.IsNullOrWhiteSpace(redirectUri) || string.IsNullOrWhiteSpace(codeChallenge))
            return Results.BadRequest("Missing authorization parameters.");

        // 1. API-key allowlist gate (auto-passes when RequireMcpApiKey is off).
        if (!allowlist.IsAllowed(apiKey))
        {
            logger.LogWarning("OAuth authorize denied: API key not in allowlist.");
            return Html(RenderForm(clientId, redirectUri, state, codeChallenge, allowlist.RequireApiKey,
                "Invalid API key."), StatusCodes.Status401Unauthorized);
        }

        // 2. Validate the Listonic credentials by a real login — classify bad-creds vs API error.
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return Html(RenderForm(clientId, redirectUri, state, codeChallenge, allowlist.RequireApiKey,
                "Listonic email and password are required."), StatusCodes.Status400BadRequest);

        var outcome = await loginValidator.ValidateAsync(email, password, ct);
        switch (outcome.Kind)
        {
            case LoginOutcomeKind.BadCredentials:
                logger.LogInformation("OAuth authorize: Listonic credentials rejected.");
                return Html(RenderForm(clientId, redirectUri, state, codeChallenge, allowlist.RequireApiKey,
                    "Listonic rejected these credentials. Check your email and password."),
                    StatusCodes.Status401Unauthorized);
            case LoginOutcomeKind.ApiError:
                logger.LogWarning("OAuth authorize: Listonic API error during validation ({Detail}).", outcome.Detail);
                return Html(RenderForm(clientId, redirectUri, state, codeChallenge, allowlist.RequireApiKey,
                    "Listonic service is temporarily unavailable. Please try again."),
                    StatusCodes.Status503ServiceUnavailable);
        }

        // 3. Mint a single-use, short-lived authorization code binding the creds + PKCE + redirect.
        // When no API key is required, there is no key to hash — embed an empty hash (IsHashAllowed
        // passes it through in that mode).
        var apiKeyHash = allowlist.RequireApiKey ? CredentialTokenService.HashApiKey(apiKey!) : string.Empty;
        var code = tokens.Protect(new TokenPayload(
            TokenKind.Code, email!, password!, apiKeyHash,
            codeChallenge, redirectUri), CodeTtl);

        logger.LogInformation("OAuth authorize: credentials validated, issuing authorization code.");

        var sep = redirectUri.Contains('?') ? '&' : '?';
        var location = $"{redirectUri}{sep}code={Uri.EscapeDataString(code)}";
        if (!string.IsNullOrEmpty(state))
            location += $"&state={Uri.EscapeDataString(state)}";
        return Results.Redirect(location);
    }

    private static async Task<IResult> Token(
        HttpContext ctx,
        ApiKeyAllowlist allowlist,
        CredentialTokenService tokens,
        ILogger<Marker> logger,
        CancellationToken ct)
    {
        var form = await ctx.Request.ReadFormAsync(ct);
        string? F(string k) => form[k].FirstOrDefault();
        var grantType = F("grant_type");

        return grantType switch
        {
            "authorization_code" => ExchangeCode(F, allowlist, tokens, logger),
            "refresh_token" => Refresh(F, allowlist, tokens, logger),
            _ => TokenError("unsupported_grant_type"),
        };
    }

    private static IResult ExchangeCode(
        Func<string, string?> f, ApiKeyAllowlist allowlist, CredentialTokenService tokens, ILogger<Marker> logger)
    {
        var code = f("code");
        var verifier = f("code_verifier");
        var redirectUri = f("redirect_uri");

        var payload = tokens.TryUnprotect(code, TokenKind.Code);
        if (payload is null)
        {
            logger.LogWarning("OAuth token: authorization code invalid or expired.");
            return TokenError("invalid_grant");
        }

        if (!Pkce.VerifyS256(verifier, payload.CodeChallenge))
        {
            logger.LogWarning("OAuth token: PKCE verification failed.");
            return TokenError("invalid_grant");
        }

        if (!string.Equals(redirectUri, payload.RedirectUri, StringComparison.Ordinal))
        {
            logger.LogWarning("OAuth token: redirect_uri mismatch.");
            return TokenError("invalid_grant");
        }

        // Re-check the key is still allowlisted at issue time too.
        if (!allowlist.IsHashAllowed(payload.ApiKeyHash))
        {
            logger.LogWarning("OAuth token: API key no longer allowlisted; refusing to issue tokens.");
            return TokenError("invalid_grant");
        }

        logger.LogInformation("OAuth token: issuing access + refresh tokens.");
        return IssueTokens(payload, tokens);
    }

    private static IResult Refresh(
        Func<string, string?> f, ApiKeyAllowlist allowlist, CredentialTokenService tokens, ILogger<Marker> logger)
    {
        var refreshToken = f("refresh_token");
        var payload = tokens.TryUnprotect(refreshToken, TokenKind.Refresh);
        if (payload is null)
        {
            logger.LogWarning("OAuth refresh: refresh token invalid or expired.");
            return TokenError("invalid_grant");
        }

        // The kill-switch: a pulled API key invalidates refresh too.
        if (!allowlist.IsHashAllowed(payload.ApiKeyHash))
        {
            logger.LogWarning("OAuth refresh: API key no longer allowlisted; session ended.");
            return TokenError("invalid_grant");
        }

        logger.LogInformation("OAuth refresh: re-minting access token (rotating refresh).");
        return IssueTokens(payload, tokens);
    }

    private static IResult IssueTokens(TokenPayload payload, CredentialTokenService tokens)
    {
        var access = tokens.Protect(
            payload with { Kind = TokenKind.Access, CodeChallenge = null, RedirectUri = null }, AccessTtl);
        var refresh = tokens.Protect(
            payload with { Kind = TokenKind.Refresh, CodeChallenge = null, RedirectUri = null }, RefreshTtl);

        return Results.Json(new Dictionary<string, object>
        {
            ["access_token"] = access,
            ["token_type"] = "Bearer",
            ["expires_in"] = (int)AccessTtl.TotalSeconds,
            ["refresh_token"] = refresh,
        });
    }

    private static IResult TokenError(string error) =>
        Results.Json(new Dictionary<string, string> { ["error"] = error },
            statusCode: StatusCodes.Status400BadRequest);

    private static string Issuer(HttpContext ctx)
    {
        var req = ctx.Request;
        return $"{req.Scheme}://{req.Host}{req.PathBase}";
    }

    private static IResult Html(string html, int statusCode = StatusCodes.Status200OK) =>
        Results.Content(html, "text/html; charset=utf-8", Encoding.UTF8, statusCode);

    private static string RenderForm(
        string? clientId, string redirectUri, string? state, string codeChallenge, bool requireApiKey, string? error)
    {
        string H(string? v) => WebUtility.HtmlEncode(v ?? string.Empty);
        var errorBlock = error is null ? "" : $"<p class=\"error\">{H(error)}</p>";

        var apiKeyBlock = requireApiKey
            ? """
                <label for="mcp-api-key">MCP server API key</label>
                <input id="mcp-api-key" name="api_key" type="password"
                       autocapitalize="none" spellcheck="false" required>
                <p class="field-hint">Provided by whoever runs this MCP server (not your Listonic password).</p>
            """
            : "";

        return $$"""
            <!doctype html>
            <html lang="en"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <meta name="color-scheme" content="light dark">
            <title>Unofficial Listonic MCP — Sign in</title>
            <link rel="icon" href="/favicon.ico" sizes="16x16 32x32 48x48">
            <link rel="icon" href="/icon-192.png" type="image/png" sizes="192x192">
            <link rel="apple-touch-icon" href="/apple-touch-icon.png">
            <style>
              body{font-family:system-ui,sans-serif;background:#f4f5f7;margin:0;padding:2rem;color:#1a1a2e}
              .card{max-width:24rem;margin:3rem auto;background:#fff;border-radius:12px;padding:1.75rem;
                    box-shadow:0 2px 12px rgba(0,0,0,.08)}
              h1{font-size:1.2rem;margin:0 0 1rem} label{display:block;margin:.75rem 0 .25rem;font-size:.85rem;font-weight:600}
              input{width:100%;box-sizing:border-box;padding:.6rem;border:1px solid #cbd0d8;border-radius:8px;font-size:1rem;
                    background:#fff;color:#1a1a2e}
              button{margin-top:1.25rem;width:100%;padding:.7rem;border:0;border-radius:8px;background:#1f9d3a;color:#fff;
                     font-size:1rem;font-weight:600;cursor:pointer} button:hover{background:#198232}
              button:disabled{opacity:.65;cursor:progress} input:read-only{opacity:.65}
              .error{background:#fde8e8;color:#9b1c1c;padding:.6rem .75rem;border-radius:8px;font-size:.85rem}
              .field-hint{color:#667085;font-size:.72rem;margin:.3rem 0 0}
              .hint{color:#667085;font-size:.78rem;margin-top:1rem}
              @media (prefers-color-scheme: dark){
                body{background:#0f1117;color:#e6e8ee}
                .card{background:#1a1d27;box-shadow:0 2px 12px rgba(0,0,0,.4)}
                input{background:#0f1117;color:#e6e8ee;border-color:#3a3f4b}
                input::placeholder{color:#8b90a0}
                button{background:#2bb24c} button:hover{background:#3cc45d}
                .error{background:#3a1b1d;color:#f3b4b4}
                .field-hint,.hint{color:#9aa0b0}
              }
            </style></head>
            <body><div class="card">
              <h1>Connect Listonic (unofficial)</h1>
              {{errorBlock}}
              <form id="login" method="post" action="/authorize">
                <input type="hidden" name="client_id" value="{{H(clientId)}}">
                <input type="hidden" name="redirect_uri" value="{{H(redirectUri)}}">
                <input type="hidden" name="state" value="{{H(state)}}">
                <input type="hidden" name="code_challenge" value="{{H(codeChallenge)}}">

                <label for="listonic_email">Listonic account email</label>
                <input id="listonic_email" name="listonic_email" type="email"
                       autocomplete="username" autocapitalize="none" spellcheck="false" required>

                <label for="listonic_password">Listonic account password</label>
                <input id="listonic_password" name="listonic_password" type="password"
                       autocomplete="current-password" required>
            {{apiKeyBlock}}
                <button id="submit" type="submit">Sign in</button>
              </form>
              <p class="hint">Your Listonic credentials are used to sign in on your behalf and are stored only
              inside your encrypted session token — never on the server.</p>
            </div>
            <script>
              // Loading state: lock the form while the Listonic login runs (it can take a few seconds).
              // Inputs go read-only, not disabled — disabled fields would be left out of the POST.
              const form = document.getElementById('login');
              const button = document.getElementById('submit');
              const fields = form.querySelectorAll('input:not([type=hidden])');
              const setBusy = busy => {
                fields.forEach(f => f.readOnly = busy);
                button.disabled = busy;
                button.textContent = busy ? 'Signing in…' : 'Sign in';
                form.setAttribute('aria-busy', String(busy));
              };
              form.addEventListener('submit', e => {
                if (button.disabled) { e.preventDefault(); return; } // swallow double submits
                setBusy(true);
              });
              // Back/forward cache restores the locked page — unlock it.
              window.addEventListener('pageshow', e => { if (e.persisted) setBusy(false); });
            </script>
            </body></html>
            """;
    }

    /// <summary>Marker type so endpoint handlers can resolve a categorised <see cref="ILogger{T}"/>.</summary>
    public sealed class Marker;

    public sealed class ClientRegistrationRequest
    {
        [System.Text.Json.Serialization.JsonPropertyName("redirect_uris")]
        public string[] RedirectUris { get; set; } = [];
    }
}
