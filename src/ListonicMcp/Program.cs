using ListonicApiClient;
using ListonicMcp;
using ListonicMcp.Listonic;
using ListonicMcp.Security;
using ListonicMcp.Tools;

if (args.Contains("--stdio"))
{
    await StdioHost.RunAsync(args);
    return;
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<IListonicCredentialProvider, HttpContextCredentialProvider>();
builder.Services.AddListonicApiClient();

builder.Services
    .AddMcpServer(options => options.ServerInstructions = McpInstructions.Text)
    .WithHttpTransport()
    .WithTools<ListonicTools>();

var app = builder.Build();

// Fail fast on invalid configuration with a readable message (and exit code 1) before anything is
// served. Host start would also run these ValidateOnStart checks, but it logs a stack trace first.
if (!StartupValidation.TryValidate(app.Services))
    return;

app.UseWhen(
    ctx => ctx.Request.Path.StartsWithSegments("/mcp"),
    branch => branch.UseMiddleware<ApiKeyMiddleware>());

app.MapMcp("/mcp");

app.Logger.LogInformation("ListonicMcp v{Version} starting",
    typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "unknown");

app.Run();
