using Microsoft.Extensions.Options;

namespace ListonicMcp;

/// <summary>Binds the <c>Telemetry</c> section (OpenTelemetry OTLP export; web host only).</summary>
public sealed class TelemetryOptions
{
    /// <summary>Enables OTLP export of traces and logs. Default <c>false</c>.</summary>
    public bool Enabled { get; set; }

    /// <summary>Absolute OTLP endpoint URI (e.g. <c>http://alloy:4317</c>). Required when enabled.</summary>
    public string? OtlpEndpoint { get; set; }
}

/// <summary>Requests not worth a trace: static icons and OAuth discovery documents (fetched by clients on every connect).</summary>
public static class TelemetryNoise
{
    public static bool IsNoise(PathString path) =>
        path.StartsWithSegments("/.well-known")
        || path.Equals("/favicon.ico", StringComparison.OrdinalIgnoreCase)
        || path.Equals("/apple-touch-icon.png", StringComparison.OrdinalIgnoreCase)
        || (path.Value is { } p && p.StartsWith("/icon-", StringComparison.OrdinalIgnoreCase)
                                && p.EndsWith(".png", StringComparison.OrdinalIgnoreCase));
}

/// <summary>When telemetry is enabled, the OTLP endpoint must be an absolute http(s) URI.</summary>
public sealed class TelemetryOptionsValidator : IValidateOptions<TelemetryOptions>
{
    public ValidateOptionsResult Validate(string? name, TelemetryOptions options)
    {
        if (!options.Enabled)
            return ValidateOptionsResult.Success;

        if (!IsValidEndpoint(options.OtlpEndpoint, out _))
            return ValidateOptionsResult.Fail(
                "Telemetry:OtlpEndpoint must be an absolute http(s) URI (e.g. http://alloy:4317) when " +
                $"Telemetry:Enabled is true (got '{options.OtlpEndpoint}').");

        return ValidateOptionsResult.Success;
    }

    /// <summary>
    /// Absolute http/https only — on Unix <c>Uri.TryCreate</c> also accepts "/path" (as file://) and
    /// "host:port" (as a "host" scheme), neither of which the OTLP exporter can use.
    /// </summary>
    public static bool IsValidEndpoint(string? value, out Uri? uri) =>
        Uri.TryCreate(value, UriKind.Absolute, out uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
