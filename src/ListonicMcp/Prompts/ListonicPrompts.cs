using System.ComponentModel;
using ModelContextProtocol.Server;

namespace ListonicMcp.Prompts;

/// <summary>
/// User-invoked shortcuts for everyday shopping-list flows. MCP prompts never run on their own — the user
/// picks one (a slash command in Claude Code, the "+" menu in claude.ai). Each returns a user message telling
/// the model which tools to call. They are defaults: the closing sentence asks the model to prefer the user's
/// own instructions or skills (a hint only — nothing enforces it), and <c>Prompts__Enabled=false</c> removes them.
/// </summary>
[McpServerPromptType]
public sealed class ListonicPrompts
{
    internal const string ListArgumentDescription = "The shopping list's name, as shown by get_lists.";

    internal const string Defer =
        "These are default steps: if I have my own instructions or a skill for Listonic shopping lists, follow those preferences instead.";

    private static string FindList(string list) =>
        $"Call get_lists and find the list named \"{list}\" (if more than one could match, ask me which), then call get_items for it.";

    [McpServerPrompt(Name = "what_to_buy", Title = "What's left to buy")]
    [Description("Show only the items still to buy on a shopping list.")]
    public static string WhatToBuy([Description(ListArgumentDescription)] string list) =>
        $"""
        Show what's still to buy on my Listonic shopping list "{list}".
        1. {FindList(list)}
        2. Reply with only the unchecked items as a short checklist (name and quantity), keeping the items' language.
           Don't list the checked items — just say how many are already bought.
        {Defer}
        """;

    [McpServerPrompt(Name = "quick_add", Title = "Add items")]
    [Description("Add items written as free text (e.g. \"2 l mleka, chleb, 500 g sera\") to a shopping list, without duplicates.")]
    public static string QuickAdd(
        [Description(ListArgumentDescription)] string list,
        [Description("What to add, as free text, e.g. \"2 l mleka, chleb, 500 g sera\".")] string items) =>
        $"""
        Add these to my Listonic shopping list "{list}": {items}
        1. {FindList(list)}
        2. Split the text into separate products. Put only the product name in name (keep my language), the number in
           amount and the unit in unit.
        3. For each product: if a checked row with that name exists, untick it and set the new amount (edit_items); if an
           unchecked row exists, ask me whether to add to its amount or replace it; otherwise add it.
        4. Use one add_items call and at most one edit_items call, then show me what changed.
        {Defer}
        """;

    [McpServerPrompt(Name = "bought", Title = "I bought…")]
    [Description("Tick off what you bought on a shopping list (or everything still unchecked).")]
    public static string Bought(
        [Description(ListArgumentDescription)] string list,
        [Description("What you bought, as free text. Leave empty to tick off everything still unchecked.")] string? items = null) =>
        $"""
        On my Listonic shopping list "{list}", tick off what I bought: {(string.IsNullOrWhiteSpace(items)
            ? "everything still unchecked — tell me how many items that is and ask me to confirm first"
            : items)}.
        1. {FindList(list)}
        2. Match each product to the unchecked rows; several rows can share a name — tick every matching unchecked row.
           Never delete anything.
        3. Use one edit_items call with isChecked: true, then report what was ticked off and anything you couldn't match.
        {Defer}
        """;

    [McpServerPrompt(Name = "clear_bought", Title = "Clear bought items")]
    [Description("Delete the items already ticked off on a shopping list, after confirmation.")]
    public static string ClearBought([Description(ListArgumentDescription)] string list) =>
        $"""
        Clear the bought items from my Listonic shopping list "{list}".
        1. {FindList(list)}
        2. Tell me how many checked items there are, list them briefly, and ask me to confirm — deleting can't be undone.
        3. Only after I confirm, delete just the checked items with one delete_items call (with confirmName for each).
           Unchecked items stay on the list.
        {Defer}
        """;

    [McpServerPrompt(Name = "share_as_text", Title = "List as a message")]
    [Description("Format the items still to buy as plain text to paste into a chat app.")]
    public static string ShareAsText([Description(ListArgumentDescription)] string list) =>
        $"""
        Format my Listonic shopping list "{list}" as a plain-text message I can paste into a chat app.
        1. {FindList(list)} This is read-only — don't change anything.
        2. Output only the unchecked items, the list name on the first line, then one item per line as
           "- name (quantity)" (no quantity in brackets if there is none). No ids, no checked items, no other formatting.
        {Defer}
        """;
}
