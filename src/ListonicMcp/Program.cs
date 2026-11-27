using ListonicApiClient;
using ListonicMcp;
using ListonicMcp.Listonic;
using ListonicMcp.Security;
using ListonicMcp.Security.OAuth;
using ListonicMcp.Prompts;
using ListonicMcp.Tools;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

if (args.Contains("--stdio"))
{
    await StdioHost.RunAsync(args);
    return;
}

var builder = WebApplication.CreateBuilder(args);

var resource = ResourceBuilder.CreateDefault()
    .AddService("listonic-mcp",
        serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "unknown");

// Telemetry is decided at build time (it changes what gets registered), so read it eagerly here;
// the same section is also registered as validated options below, so a bad endpoint fails at start.
var telemetrySection = builder.Configuration.GetSection("Telemetry");
var telemetry = telemetrySection.Get<TelemetryOptions>() ?? new TelemetryOptions();
builder.Services.AddOptions<TelemetryOptions>().Bind(telemetrySection).ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<TelemetryOptions>, TelemetryOptionsValidator>();

if (telemetry.Enabled)
{
    // Only applied when valid — an invalid endpoint is reported by TelemetryOptionsValidator instead.
    TelemetryOptionsValidator.IsValidEndpoint(telemetry.OtlpEndpoint, out var otlpEndpoint);

    builder.Services.AddOpenTelemetry()
        .WithTracing(tracing => tracing
            .SetResourceBuilder(resource)
            .AddAspNetCoreInstrumentation(o =>
            {
                // Attach unhandled exceptions (type, message, stack trace) to the request span.
                o.RecordException = true;
                o.Filter = ctx => !TelemetryNoise.IsNoise(ctx.Request.Path);
            })
            // MCP SDK spans: one per JSON-RPC request, e.g. "tools/call get_items" (tag gen_ai.tool.name).
            .AddSource("Experimental.ModelContextProtocol")
            // .NET built-in HttpClient spans: outbound Listonic API calls (query strings stay redacted).
            .AddSource("System.Net.Http")
            // App spans: listonic.login; business tags on tool spans.
            .AddSource(ListonicTelemetry.SourceName)
            .AddOtlpExporter(o =>
            {
                if (otlpEndpoint is not null) o.Endpoint = otlpEndpoint;
            }))
        .WithLogging(logging => logging
            .SetResourceBuilder(resource)
            .AddOtlpExporter(o =>
            {
                if (otlpEndpoint is not null) o.Endpoint = otlpEndpoint;
            }));

    builder.Logging.AddOpenTelemetry(o =>
    {
        o.IncludeScopes = true;
        o.IncludeFormattedMessage = true;
    });
}

builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<IListonicCredentialProvider, HttpContextCredentialProvider>();
builder.Services.AddListonicApiClient(
    rateLimit: builder.Configuration.GetSection(ListonicRateLimitOptions.Section));

// OAuth authorization: API-key allowlist + encrypted stateless tokens carrying the Listonic credentials.
// SecurityOptionsValidator fails startup on a missing/placeholder/short TokenSecret or an empty
// allowlist while RequireMcpApiKey is on (HTTP host only — stdio has no OAuth).
builder.Services.AddOptions<SecurityOptions>()
    .Bind(builder.Configuration.GetSection(SecurityOptions.Section))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<SecurityOptions>, SecurityOptionsValidator>();
builder.Services.AddSingleton<ApiKeyAllowlist>();
builder.Services.AddOptions<OAuthOptions>()
    .Bind(builder.Configuration.GetSection(OAuthOptions.Section))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<OAuthOptions>, OAuthOptionsValidator>();

// Token encryption derives its key deterministically from Security:TokenSecret — nothing is
// persisted to disk, so tokens survive restarts/replicas and the secret alone controls validity.
builder.Services.AddSingleton(sp =>
    new CredentialTokenService(sp.GetRequiredService<IOptions<SecurityOptions>>().Value.TokenSecret!));

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = McpServerIdentity.Create();
        options.ServerInstructions = McpInstructions.Text;
    })
    .WithHttpTransport()
    .WithListonicTools()
    .WithListonicPrompts(builder.Configuration)
    .WithRequestFilters(filters => filters.AddCallToolFilter(ClientContextFilter.Create));

var app = builder.Build();

// Fail fast on invalid configuration with a readable message (and exit code 1) before anything is
// served. Host start would also run these ValidateOnStart checks, but it logs a stack trace first.
if (!StartupValidation.TryValidate(app.Services))
    return;

// Honor X-Forwarded-Proto/Host from the reverse proxy that terminates TLS, so Request.Scheme is
// "https" and the OAuth discovery metadata advertises https:// endpoints. The container is only
// reachable via the proxy, so the single forwarding hop is trusted (no known-proxy restriction).
var forwardedHeaders = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost,
};
forwardedHeaders.KnownIPNetworks.Clear();
forwardedHeaders.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeaders);

// Favicon / touch icons (wwwroot) for the OAuth sign-in page and for clients that look up a
// connector's icon from the server origin. Public — served before the /mcp gate.
app.UseStaticFiles();

app.UseMiddleware<RequestLoggingMiddleware>();

// OAuth authorization-server + protected-resource endpoints (public — no /mcp gate).
app.MapOAuthEndpoints();

app.UseWhen(
    ctx => ctx.Request.Path.StartsWithSegments("/mcp"),
    branch =>
    {
        branch.UseMiddleware<ApiKeyMiddleware>();
        branch.UseMiddleware<UserScopeMiddleware>();
    });

app.MapMcp("/mcp");

app.Logger.LogInformation("ListonicMcp v{Version} starting",
    typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "unknown");

app.Run();

/// <summary>Visible to <c>WebApplicationFactory&lt;Program&gt;</c> in the in-memory OAuth flow tests.</summary>
public partial class Program;
