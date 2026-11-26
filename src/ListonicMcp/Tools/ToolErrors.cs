using System.Net;
using ListonicApiClient;
using ModelContextProtocol;
using Refit;

namespace ListonicMcp.Tools;

/// <summary>
/// Turns Listonic failures into plain-language tool errors. The MCP SDK reports any other exception as
/// an opaque "An error occurred invoking '&lt;tool&gt;'", so each tool body runs through
/// <see cref="GuardAsync"/>: known failures are logged, marked on the span as an error, and rethrown as
/// <see cref="McpException"/>, whose message the SDK passes to the model as the tool result.
/// This is also how rejected credentials surface — the SSE response has already started, so an HTTP
/// 401 can't be sent at that point.
/// </summary>
internal static class ToolErrors
{
    internal sealed record Mapped(string Outcome, string Message);

    public const string CredentialsRejected =
        "Listonic rejected the account's sign-in (the password may have changed). Reconnect this Listonic " +
        "connector in the AI client's settings (disconnect and connect it again), or update the credentials " +
        "it uses, then retry.";

    public static async Task<string> GuardAsync(string tool, ILogger logger, Func<Task<string>> body)
    {
        try
        {
            return await body();
        }
        catch (Exception ex) when (Map(ex) is { } mapped)
        {
            logger.LogWarning(ex, "{Tool} failed: {Outcome}", tool, mapped.Outcome);
            ListonicTelemetry.Fail(mapped.Outcome, mapped.Message);
            throw new McpException(mapped.Message);
        }
    }

    /// <summary>Maps an exception (or any inner exception) to an outcome + message; null leaves it unhandled.</summary>
    internal static Mapped? Map(Exception ex)
    {
        if (ex is OperationCanceledException)
            return null;

        for (var e = ex; e is not null; e = e.InnerException)
            if (e is ListonicCredentialsRejectedException)
                return new("credentials_rejected", CredentialsRejected);

        for (var e = ex; e is not null; e = e.InnerException)
            if (e is ApiException api)
                return FromStatus(api.StatusCode);

        for (var e = ex; e is not null; e = e.InnerException)
            if (e is HttpRequestException)
                return new("unavailable",
                    "Listonic could not be reached or failed to sign in right now. Try again in a moment.");

        return null;
    }

    private static Mapped FromStatus(HttpStatusCode status)
    {
        var code = (int)status;
        return status switch
        {
            HttpStatusCode.NotFound => new("not_found",
                $"Listonic couldn't find that list or item (HTTP {code}). It may have been deleted, or the list " +
                "is no longer shared with this account. Call get_lists / get_items again for current ids."),
            HttpStatusCode.Forbidden => new("forbidden",
                $"This account has no access to that list (HTTP {code}) — it may no longer be shared with you. " +
                "Call get_lists to see the lists you can access."),
            HttpStatusCode.Unauthorized => new("credentials_rejected", CredentialsRejected),
            HttpStatusCode.TooManyRequests => new("rate_limited",
                $"Listonic is rate-limiting requests (HTTP {code}). Wait a moment and try again."),
            _ when code >= 500 => new("unavailable",
                $"Listonic is temporarily unavailable (HTTP {code}). Try again in a moment."),
            _ => new("rejected_request",
                $"Listonic rejected the request (HTTP {code}). Check the list/item ids (get_lists / get_items) " +
                "and the values, then try again."),
        };
    }
}
