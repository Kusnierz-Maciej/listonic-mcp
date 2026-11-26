using System.Diagnostics;

namespace ListonicApiClient;

/// <summary>
/// App-specific tracing: one <see cref="ActivitySource"/> plus helpers that tag/mark the current span.
/// Everything is null-safe — with no listener (telemetry off) <see cref="ActivitySource.StartActivity(string, ActivityKind)"/>
/// returns null and <see cref="Activity.Current"/> carries no recorded span, so each call is a cheap no-op.
/// </summary>
public static class ListonicTelemetry
{
    /// <summary>ActivitySource name — subscribe with <c>tracing.AddSource(ListonicTelemetry.SourceName)</c>.</summary>
    public const string SourceName = "Listonic";

    public static readonly ActivitySource Source = new(SourceName);

    /// <summary>Span attribute keys (custom <c>listonic.*</c> namespace + OTel semconv <c>user.email</c>).</summary>
    public static class Tags
    {
        public const string UserEmail = "user.email";
        public const string ListId = "listonic.list.id";
        public const string ListName = "listonic.list.name";
        public const string ListsCount = "listonic.lists.count";
        public const string ItemsCount = "listonic.items.count";
        public const string Outcome = "listonic.outcome";
    }

    /// <summary>Sets a tag on the current span (the MCP <c>tools/call</c> span inside a tool).</summary>
    public static void Tag(string key, object? value) => Activity.Current?.SetTag(key, value);

    /// <summary>Records a business outcome on the current span (e.g. "ok", "added", "deleted").</summary>
    public static void Outcome(string outcome) => Tag(Tags.Outcome, outcome);

    /// <summary>
    /// Marks the current span as failed for a handled, non-exception failure (list not found, credentials
    /// rejected…) so Grafana's "Errors" filter and TraceQL <c>{ status = error }</c> find it.
    /// </summary>
    public static void Fail(string outcome, string description)
    {
        var activity = Activity.Current;
        if (activity is null) return;
        activity.SetTag(Tags.Outcome, outcome);
        activity.SetStatus(ActivityStatusCode.Error, description);
    }

    /// <summary>Adds a timestamped event to the current span.</summary>
    public static void Event(string name, params (string Key, object? Value)[] tags)
    {
        var activity = Activity.Current;
        if (activity is null) return;
        var collection = new ActivityTagsCollection();
        foreach (var (k, v) in tags) collection[k] = v;
        activity.AddEvent(new ActivityEvent(name, tags: collection));
    }
}
