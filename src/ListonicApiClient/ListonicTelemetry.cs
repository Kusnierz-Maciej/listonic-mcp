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
        public const string ListNewName = "listonic.list.new_name";
        public const string ListsCount = "listonic.lists.count";
        public const string ListsShared = "listonic.lists.shared";
        public const string ItemsCount = "listonic.items.count";
        public const string ItemsUnchecked = "listonic.items.unchecked";
        public const string ItemsChecked = "listonic.items.checked";
        public const string ItemNames = "listonic.items.names";
        public const string ItemIds = "listonic.items.ids";
        public const string ItemsAffected = "listonic.items.affected";
        public const string ItemsMissing = "listonic.items.missing";
        public const string ItemsTicked = "listonic.items.ticked";
        public const string ItemsUnticked = "listonic.items.unticked";
        public const string ItemsRenamed = "listonic.items.renamed";
        public const string ItemsQuantityChanged = "listonic.items.quantity_changed";
        public const string CompletionMatches = "listonic.completion.matches";
        public const string Outcome = "listonic.outcome";
    }

    /// <summary>Longest list attribute we record (names/ids); the matching count tag carries the full size.</summary>
    public const int MaxListedValues = 20;

    /// <summary>A capped string-array attribute value (first <see cref="MaxListedValues"/> entries).</summary>
    public static string[] Capped(IEnumerable<string> values) => values.Take(MaxListedValues).ToArray();

    /// <summary>
    /// Starts a child span for one phase of a tool (pre-check, write batch, re-read…) so a trace shows where time
    /// goes and which backend calls belong together. Dispose to end it. Null (free) when telemetry is off.
    /// </summary>
    public static Activity? Phase(string name, params (string Key, object? Value)[] tags)
    {
        var activity = Source.StartActivity(name);
        if (activity is not null)
            foreach (var (k, v) in tags) activity.SetTag(k, v);
        return activity;
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
