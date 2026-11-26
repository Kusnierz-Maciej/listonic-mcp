using ListonicMcp.Listonic;

namespace ListonicMcp.Tests;

public class SettingsValidationTests
{
    [Test]
    public async Task Stdio_BothCredentials_Valid()
    {
        var result = new StdioCredentialsOptionsValidator().Validate(null,
            new StdioCredentialsOptions { Email = "a@b.c", Password = "pw" });

        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Stdio_MissingCredentials_ListsBothFailures()
    {
        var result = new StdioCredentialsOptionsValidator().Validate(null, new StdioCredentialsOptions { Email = " " });

        await Assert.That(result.Failed).IsTrue();
        await Assert.That(result.Failures!.Count()).IsEqualTo(2);
    }
}

public class SecuritySettingsValidationTests
{
    private const string GoodSecret = "a-good-token-secret-that-is-32-chars+";

    private static Microsoft.Extensions.Options.ValidateOptionsResult Validate(string? secret, bool requireKey = true, params string[] keys) =>
        new ListonicMcp.Security.SecurityOptionsValidator().Validate(null,
            new ListonicMcp.Security.SecurityOptions { TokenSecret = secret, RequireMcpApiKey = requireKey, AllowedApiKeys = keys });

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("too-short")]
    [Arguments(ListonicMcp.Security.SecurityOptionsValidator.PlaceholderTokenSecret)]
    public async Task BadTokenSecret_Fails(string? secret) =>
        await Assert.That(Validate(secret, true, "key-1").Failed).IsTrue();

    [Test]
    public async Task RequireKey_WithEmptyAllowlist_Fails() =>
        await Assert.That(Validate(GoodSecret, true).Failed).IsTrue();

    [Test]
    public async Task KeysOff_WithEmptyAllowlist_Passes() =>
        await Assert.That(Validate(GoodSecret, false).Succeeded).IsTrue();

    [Test]
    public async Task GoodSecret_WithKey_Passes() =>
        await Assert.That(Validate(GoodSecret, true, "key-1").Succeeded).IsTrue();
}
