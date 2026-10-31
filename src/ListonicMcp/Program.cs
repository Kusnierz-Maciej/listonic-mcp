using ListonicApiClient;
using ListonicMcp.Security;

var builder = WebApplication.CreateBuilder(args);

var listonicOptions = new ListonicClientOptions
{
    Email    = builder.Configuration["Listonic:Email"]    ?? throw new InvalidOperationException("Listonic:Email is required"),
    Password = builder.Configuration["Listonic:Password"] ?? throw new InvalidOperationException("Listonic:Password is required"),
};
builder.Services.AddListonicApiClient(listonicOptions);

builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

var app = builder.Build();

app.UseWhen(
    ctx => ctx.Request.Path.StartsWithSegments("/mcp"),
    branch => branch.UseMiddleware<ApiKeyMiddleware>());

app.MapMcp("/mcp");

app.Logger.LogInformation("ListonicMcp v{Version} starting",
    typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "unknown");

app.Run();
