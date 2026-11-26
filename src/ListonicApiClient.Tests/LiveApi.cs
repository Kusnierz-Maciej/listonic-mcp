namespace ListonicApiClient.Tests;

/// <summary>
/// Shared constants for live tests. Every <c>[Category("LiveApi")]</c> class must also carry
/// <c>[NotInParallel(LiveApi.Serial)]</c>: they share one real account (and one client-side rate limit),
/// and the class-level "[test] " cleanup must not delete lists another class is still using.
/// </summary>
internal static class LiveApi
{
    public const string Serial = "LiveApi";
    public const string UnknownId = "999999999";
}
