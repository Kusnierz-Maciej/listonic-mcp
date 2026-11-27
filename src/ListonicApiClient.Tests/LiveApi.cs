using System.Net;
using ListonicApiClient.Models.Lists;
using Refit;

namespace ListonicApiClient.Tests;

/// <summary>
/// Shared constants and housekeeping for live tests. Every <c>[Category("LiveApi")]</c> class must also carry
/// <c>[NotInParallel(LiveApi.Serial)]</c> (one real account, one client-side rate limit, shared cleanup), call
/// <see cref="SweepOnceAtStartAsync"/> from a <c>[Before(HookType.Class)]</c> hook and
/// <see cref="SweepTestListsAsync"/> from an <c>[After(HookType.Class)]</c> hook.
/// </summary>
internal static class LiveApi
{
    public const string Serial = "LiveApi";
    public const string UnknownId = "999999999";

    /// <summary>Name prefix of every list a live test (or the sandbox) creates.</summary>
    public const string TestPrefix = "[test] ";

    private static readonly Lazy<Task> StartSweep = new(SweepTestListsAsync);

    /// <summary>
    /// Crash recovery: removes "[test] " lists left by a crashed or cancelled PREVIOUS run. Runs once per
    /// test run, from whichever live class starts first — later classes run after earlier ones have
    /// cleaned up (classes are serial), so sweeping again would be redundant.
    /// </summary>
    public static Task SweepOnceAtStartAsync() => StartSweep.Value;

    /// <summary>
    /// Deletes every list whose name starts with <see cref="TestPrefix"/> — the normal after-class cleanup.
    /// A list that is already gone (404) is ignored.
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
