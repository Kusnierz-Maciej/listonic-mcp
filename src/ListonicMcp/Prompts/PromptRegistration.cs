using ListonicApiClient;
using ListonicApiClient.Models.Lists;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ListonicMcp.Prompts;

/// <summary>
/// Registers <see cref="ListonicPrompts"/> and list-name completions for their <c>list</c> argument, unless
/// <c>Prompts:Enabled</c> is <c>false</c> (then the server advertises no prompts at all).
/// </summary>
internal static class PromptRegistration
{
    public const string EnabledKey = "Prompts:Enabled";
    private const int MaxCompletions = 100;

    public static IMcpServerBuilder WithListonicPrompts(this IMcpServerBuilder builder, IConfiguration configuration)
    {
        if (!configuration.GetValue(EnabledKey, true))
            return builder;

        return builder
            .WithPrompts<ListonicPrompts>()
            .WithCompleteHandler(CompleteAsync);
    }

    /// <summary>Completes the <c>list</c> argument of any prompt with the account's list names.</summary>
    internal static async ValueTask<CompleteResult> CompleteAsync(
        RequestContext<CompleteRequestParams> request, CancellationToken ct)
    {
        if (request.Params?.Ref is not PromptReference || request.Params.Argument.Name != "list")
            return Empty();

        try
        {
            var api = request.Services!.GetRequiredService<IListonicApi>();
            var lists = await api.GetListsAsync(new GetListsRequest { IncludeShares = true }, ct);
            return Complete(lists.Select(l => l.Name), request.Params.Argument.Value);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Completions are a convenience: never fail the request (e.g. credentials not available yet).
            request.Services?.GetService<ILogger<ListonicPrompts>>()
                ?.LogWarning(ex, "List-name completion failed; returning no suggestions.");
            return Empty();
        }
    }

    /// <summary>Names starting with the typed text first, then names containing it; case-insensitive, distinct.</summary>
    internal static CompleteResult Complete(IEnumerable<string> names, string? typed)
    {
        var text = typed?.Trim() ?? "";
        var matches = names
            .Where(n => !string.IsNullOrWhiteSpace(n) && n.Contains(text, StringComparison.CurrentCultureIgnoreCase))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(n => n.StartsWith(text, StringComparison.CurrentCultureIgnoreCase) ? 0 : 1)
            .ThenBy(n => n, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return new CompleteResult
        {
            Completion = new Completion
            {
                Values = matches.Take(MaxCompletions).ToList(),
                Total = matches.Count,
                HasMore = matches.Count > MaxCompletions,
            },
        };
    }

    private static CompleteResult Empty() => new() { Completion = new Completion { Values = [] } };
}
