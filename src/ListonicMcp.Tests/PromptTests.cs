using ListonicMcp.Prompts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace ListonicMcp.Tests;

public class PromptTests
{
    private static List<McpServerPrompt> Registered(bool? enabled)
    {
        var settings = new Dictionary<string, string?>();
        if (enabled is not null) settings[PromptRegistration.EnabledKey] = enabled.Value.ToString();
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMcpServer().WithListonicPrompts(config);
        return services.BuildServiceProvider().GetServices<McpServerPrompt>().ToList();
    }

    [Test]
    public async Task EnabledByDefault_FivePrompts_WithTitles()
    {
        var prompts = Registered(enabled: null);

        await Assert.That(prompts.Select(p => p.ProtocolPrompt.Name).Order())
            .IsEquivalentTo(new[] { "bought", "clear_bought", "quick_add", "share_as_text", "what_to_buy" });
        await Assert.That(prompts.All(p => !string.IsNullOrWhiteSpace(p.ProtocolPrompt.Title))).IsTrue();
        await Assert.That(prompts.All(p => p.ProtocolPrompt.Arguments!.Any(a => a.Name == "list" && a.Required == true))).IsTrue();
    }

    [Test]
    public async Task Disabled_RegistersNothing() =>
        await Assert.That(Registered(enabled: false)).IsEmpty();

    [Test]
    public async Task Texts_NameTheList_TheTools_AndDeferToUserPreferences()
    {
        foreach (var text in new[]
                 {
                     ListonicPrompts.WhatToBuy("Zakupy"), ListonicPrompts.QuickAdd("Zakupy", "2 l mleka"),
                     ListonicPrompts.Bought("Zakupy", "mleko"), ListonicPrompts.ClearBought("Zakupy"),
                     ListonicPrompts.ShareAsText("Zakupy"),
                 })
        {
            await Assert.That(text).Contains("\"Zakupy\"");
            await Assert.That(text).Contains("get_lists");
            await Assert.That(text).EndsWith(ListonicPrompts.Defer);
        }
    }

    [Test]
    public async Task Writes_UseTheRightTools_AndConfirmWhereDestructive()
    {
        await Assert.That(ListonicPrompts.QuickAdd("L", "chleb")).Contains("add_items");
        await Assert.That(ListonicPrompts.Bought("L", "chleb")).Contains("Never delete");
        await Assert.That(ListonicPrompts.Bought("L")).Contains("ask me to confirm");
        await Assert.That(ListonicPrompts.ClearBought("L")).Contains("Only after I confirm");
        await Assert.That(ListonicPrompts.ShareAsText("L")).Contains("read-only");
    }

    [Test]
    public async Task Completion_PrefixFirst_ThenContains_CaseInsensitive_Distinct()
    {
        var result = PromptRegistration.Complete(["Wspólna", "Zakupy", "zakupy", "Duże zakupy", "Apteka"], "zak");

        await Assert.That(result.Completion.Values).IsEquivalentTo(new[] { "Zakupy", "Duże zakupy" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(result.Completion.Total).IsEqualTo(2);
        await Assert.That(result.Completion.HasMore).IsFalse();
    }

    [Test]
    public async Task Completion_EmptyText_ReturnsAll() =>
        await Assert.That(PromptRegistration.Complete(["B", "a"], "").Completion.Values.Count).IsEqualTo(2);
}
