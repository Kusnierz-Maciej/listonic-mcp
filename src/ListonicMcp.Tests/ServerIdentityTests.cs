namespace ListonicMcp.Tests;

public class ServerIdentityTests
{
    [Test]
    public async Task ServerInfo_IsMarkedUnofficial_WithInlinedIcons()
    {
        var info = McpServerIdentity.Create();

        await Assert.That(info.Title).IsEqualTo("Listonic (unofficial)");
        await Assert.That(info.Icons!.Count).IsEqualTo(2);
        await Assert.That(info.Icons!.All(i => i.Source.StartsWith("data:image/png;base64,"))).IsTrue();
    }
}
