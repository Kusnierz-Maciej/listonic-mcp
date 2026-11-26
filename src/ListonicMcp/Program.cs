using ListonicApiClient;
using ListonicMcp;
using ListonicMcp.Listonic;
using ListonicMcp.Security;
using ListonicMcp.Security.OAuth;
using ListonicMcp.Tools;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

if (args.Contains("--stdio"))
{
    await StdioHost.RunAsync(args);
    return;
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<IListonicCredentialProvider, HttpContextCredentialProvider>();
builder.Services.AddListonicApiClient();

// OAuth authorization: API-key allowlist + encrypted stateless tokens carrying the Listonic credentials.
// SecurityOptionsValidator fails startup on a missing/placeholder/short TokenSecret or an empty
// allowlist while RequireMcpApiKey is on (HTTP host only — stdio has no OAuth).
builder.Services.AddOptions<SecurityOptions>()
    .Bind(builder.Configuration.GetSection(SecurityOptions.Section))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<SecurityOptions>, SecurityOptionsValidator>();
builder.Services.AddSingleton<ApiKeyAllowlist>();

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
    .WithTools<ListonicTools>();

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

// OAuth authorization-server + protected-resource endpoints (public — no /mcp gate).
app.MapOAuthEndpoints();

app.UseWhen(
    ctx => ctx.Request.Path.StartsWithSegments("/mcp"),
    branch => branch.UseMiddleware<ApiKeyMiddleware>());

app.MapMcp("/mcp");

app.Logger.LogInformation("ListonicMcp v{Version} starting",
    typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "unknown");

app.Run();
