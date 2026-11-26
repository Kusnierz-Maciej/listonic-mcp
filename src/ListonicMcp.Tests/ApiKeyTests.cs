using ListonicMcp.Security;
using Microsoft.Extensions.Options;

namespace ListonicMcp.Tests;

public class ApiKeyTests
{
    private static ApiKeyAllowlist Allowlist(params string[] keys) =>
        new(Options.Create(new SecurityOptions { AllowedApiKeys = keys }));

    [Test]
    public async Task Allowlist_AcceptsConfiguredKey_RejectsOthers()
    {
        var allowlist = Allowlist("key-a", "", "key-b");

        await Assert.That(allowlist.IsAllowed("key-b")).IsTrue();
        await Assert.That(allowlist.IsAllowed("key-c")).IsFalse();
        await Assert.That(allowlist.IsAllowed("")).IsFalse();
        await Assert.That(allowlist.IsAllowed(null)).IsFalse();
    }

    [Test]
    public async Task Validator_RequiresAtLeastOneKey()
    {
        var validator = new SecurityOptionsValidator();

        await Assert.That(validator.Validate(null, new SecurityOptions { AllowedApiKeys = [" "] }).Failed).IsTrue();
        await Assert.That(validator.Validate(null, new SecurityOptions { AllowedApiKeys = ["k"] }).Succeeded).IsTrue();
    }
}

public class OptionalApiKeyTests
{
    [Test]
    public async Task KeysOff_AllowsAnything_AndNeedsNoConfiguredKey()
    {
        var options = new SecurityOptions { RequireMcpApiKey = false };
        var allowlist = new ApiKeyAllowlist(Options.Create(options));

        await Assert.That(allowlist.RequireApiKey).IsFalse();
        await Assert.That(allowlist.IsAllowed(null)).IsTrue();
        await Assert.That(new SecurityOptionsValidator().Validate(null, options).Succeeded).IsTrue();
    }
}
