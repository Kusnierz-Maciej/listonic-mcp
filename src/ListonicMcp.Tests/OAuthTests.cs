using ListonicMcp.Security;
using ListonicMcp.Security.OAuth;
using Microsoft.Extensions.Options;

namespace ListonicMcp.Tests;

/// <summary>
/// Unit tests for the OAuth core: encrypted-token round-trip + kind/expiry/tamper handling, the API-key
/// allowlist (incl. the hash kill-switch), and PKCE S256 verification. No HTTP, no network.
/// </summary>
public class OAuthTests
{
    private const string Secret = "test-token-secret-at-least-32-chars-long";

    private static CredentialTokenService NewTokenService(string secret = Secret) =>
        new(secret);

    private static TokenPayload Access(string apiKey = "key-1") =>
        new(TokenKind.Access, "user@example.com", "pw", CredentialTokenService.HashApiKey(apiKey), null, null);

    // ─── CredentialTokenService ────────────────────────────────────────────────────────────────────

    [Test]
    public async Task Token_RoundTrips()
    {
        var svc = NewTokenService();
        var token = svc.Protect(Access(), TimeSpan.FromHours(1));
        var back = svc.TryUnprotect(token, TokenKind.Access);

        await Assert.That(back).IsNotNull();
        await Assert.That(back!.Email).IsEqualTo("user@example.com");
        await Assert.That(back.Password).IsEqualTo("pw");
    }

    [Test]
    public async Task Token_WrongKind_IsRejected()
    {
        var svc = NewTokenService();
        var access = svc.Protect(Access(), TimeSpan.FromHours(1));
        // Unprotecting an access token as a refresh token must fail (separate protector purpose + Kind check).
        await Assert.That(svc.TryUnprotect(access, TokenKind.Refresh)).IsNull();
    }

    [Test]
    public async Task Token_Tampered_IsRejected()
    {
        var svc = NewTokenService();
        var token = svc.Protect(Access(), TimeSpan.FromHours(1));
        var tampered = token[..^2] + (token[^1] == 'A' ? "BB" : "AA");
        await Assert.That(svc.TryUnprotect(tampered, TokenKind.Access)).IsNull();
    }

    [Test]
    public async Task Token_Expired_IsRejected()
    {
        var svc = NewTokenService();
        var token = svc.Protect(Access(), TimeSpan.FromMilliseconds(1));
        await Task.Delay(30);
        await Assert.That(svc.TryUnprotect(token, TokenKind.Access)).IsNull();
    }

    [Test]
    public async Task Token_Garbage_IsRejected()
    {
        var svc = NewTokenService();
        await Assert.That(svc.TryUnprotect("not-a-token", TokenKind.Access)).IsNull();
        await Assert.That(svc.TryUnprotect("", TokenKind.Access)).IsNull();
        await Assert.That(svc.TryUnprotect(null, TokenKind.Access)).IsNull();
    }

    [Test]
    public async Task Token_SameSecret_DecryptsAcrossInstances()
    {
        // Restart / replica simulation: a fresh service built from the same secret (no shared state,
        // nothing on disk) must decrypt a token the first one issued.
        var issuer = NewTokenService();
        var token = issuer.Protect(Access(), TimeSpan.FromHours(1));

        var other = NewTokenService();
        var back = other.TryUnprotect(token, TokenKind.Access);

        await Assert.That(back).IsNotNull();
        await Assert.That(back!.Email).IsEqualTo("user@example.com");
    }

    [Test]
    public async Task Token_DifferentSecret_IsRejected()
    {
        // Changing the secret must invalidate every issued token (the only validity knob).
        var token = NewTokenService("secret-A-at-least-32-chars-long-padding").Protect(Access(), TimeSpan.FromHours(1));
        var other = NewTokenService("secret-B-at-least-32-chars-long-padding");
        await Assert.That(other.TryUnprotect(token, TokenKind.Access)).IsNull();
    }

    // ─── ApiKeyAllowlist ───────────────────────────────────────────────────────────────────────────

    private static ApiKeyAllowlist Allowlist(string[]? keys = null, bool requireKey = true) =>
        new(Options.Create(new SecurityOptions
        {
            AllowedApiKeys = keys ?? [],
            RequireMcpApiKey = requireKey,
        }));

    [Test]
    public async Task Allowlist_AllowsConfiguredKey_RejectsOthers()
    {
        var a = Allowlist(["alpha", "bravo"]);
        await Assert.That(a.IsAllowed("alpha")).IsTrue();
        await Assert.That(a.IsAllowed("bravo")).IsTrue();
        await Assert.That(a.IsAllowed("charlie")).IsFalse();
        await Assert.That(a.IsAllowed("")).IsFalse();
        await Assert.That(a.IsAllowed(null)).IsFalse();
    }

    [Test]
    public async Task Allowlist_HashKillSwitch_FollowsCurrentConfig()
    {
        var hash = CredentialTokenService.HashApiKey("alpha");

        // While 'alpha' is configured, a token minted from it is honoured.
        await Assert.That(Allowlist(["alpha", "bravo"]).IsHashAllowed(hash)).IsTrue();
        // Remove 'alpha' (simulating the admin pulling the key + restart) → its tokens are rejected.
        await Assert.That(Allowlist(["bravo"]).IsHashAllowed(hash)).IsFalse();
    }

    [Test]
    public async Task Allowlist_RequirementDisabled_PassesEverything()
    {
        // RequireMcpApiKey=false: the key gate is off, so any (or no) key passes, as does any hash.
        var a = Allowlist(keys: [], requireKey: false);
        await Assert.That(a.RequireApiKey).IsFalse();
        await Assert.That(a.IsAllowed("anything")).IsTrue();
        await Assert.That(a.IsAllowed("")).IsTrue();
        await Assert.That(a.IsAllowed(null)).IsTrue();
        await Assert.That(a.IsHashAllowed("")).IsTrue();
        await Assert.That(a.IsHashAllowed(null)).IsTrue();
    }

    [Test]
    public async Task Allowlist_RequiredButEmpty_FailsClosed()
    {
        // RequireMcpApiKey=true with no keys configured → nothing can match → every request rejected.
        var a = Allowlist(keys: [], requireKey: true);
        await Assert.That(a.RequireApiKey).IsTrue();
        await Assert.That(a.IsEnabled).IsFalse();
        await Assert.That(a.IsAllowed("anything")).IsFalse();
        await Assert.That(a.IsAllowed("")).IsFalse();
        await Assert.That(a.IsHashAllowed(CredentialTokenService.HashApiKey("anything"))).IsFalse();
    }

    // ─── PKCE ──────────────────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task Pkce_S256_VerifiesMatchingVerifier()
    {
        // Known RFC 7636 test vector.
        const string verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        const string challenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";
        await Assert.That(Pkce.VerifyS256(verifier, challenge)).IsTrue();
        await Assert.That(Pkce.VerifyS256("wrong-verifier", challenge)).IsFalse();
        await Assert.That(Pkce.VerifyS256(verifier, null)).IsFalse();
        await Assert.That(Pkce.VerifyS256(null, challenge)).IsFalse();
    }
}
