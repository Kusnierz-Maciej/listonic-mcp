using ListonicApiClient;
using ListonicMcp;
using ListonicMcp.Listonic;
using ListonicMcp.Security;
using ListonicMcp.Tools;
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

// MCP server API-key allowlist (Security__AllowedApiKeys__N). Validated at startup (HTTP host only).
builder.Services.AddOptions<SecurityOptions>()
    .Bind(builder.Configuration.GetSection(SecurityOptions.Section))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<SecurityOptions>, SecurityOptionsValidator>();
builder.Services.AddSingleton<ApiKeyAllowlist>();

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

// Favicon / touch icons (wwwroot) for the OAuth sign-in page and for clients that look up a
// connector's icon from the server origin. Public — served before the /mcp gate.
app.UseStaticFiles();

app.UseWhen(
    ctx => ctx.Request.Path.StartsWithSegments("/mcp"),
    branch => branch.UseMiddleware<ApiKeyMiddleware>());

app.MapMcp("/mcp");

app.Logger.LogInformation("ListonicMcp v{Version} starting",
    typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "unknown");

app.Run();
