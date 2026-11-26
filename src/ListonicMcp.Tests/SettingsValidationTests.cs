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
