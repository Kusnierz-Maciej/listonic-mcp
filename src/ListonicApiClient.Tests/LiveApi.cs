using System.Net;
using ListonicApiClient.Models.Lists;
using Refit;

namespace ListonicApiClient.Tests;

/// <summary>
/// Shared constants and housekeeping for live tests. Every <c>[Category("LiveApi")]</c> class must also carry
/// <c>[NotInParallel(LiveApi.Serial)]</c> (one real account, one client-side rate limit, shared cleanup) and
/// call <see cref="SweepTestListsAsync"/> from both a <c>[Before(HookType.Class)]</c> and an
/// <c>[After(HookType.Class)]</c> hook.
/// </summary>
internal static class LiveApi
{
    public const string Serial = "LiveApi";
    public const string UnknownId = "999999999";

    /// <summary>Name prefix of every list a live test (or the sandbox) creates.</summary>
    public const string TestPrefix = "[test] ";

    /// <summary>
    /// Deletes every list whose name starts with <see cref="TestPrefix"/>. Run before a class so leftovers
    /// from a crashed or cancelled previous run don't skew counts or pile up, and after it as the normal
    /// cleanup. A list that is already gone (404) is ignored.
    /// </summary>
    public static async Task SweepTestListsAsync()
    {
        var api = ApiClientFactory.Api;
        var lists = await api.GetListsAsync(new GetListsRequest { IncludeShares = true });
        foreach (var list in lists.Where(l => l.Name.StartsWith(TestPrefix, StringComparison.Ordinal)))
        {
            try
            {
                await api.DeleteListAsync(list.Id);
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                // already deleted
            }
        }
    }
}
