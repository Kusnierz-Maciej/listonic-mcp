using ListonicMcp.Listonic;
using ListonicMcp.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<ListonicApiCache>();
builder.Services.AddScoped<ListonicApiAccessor>();

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInstructions =
            """
            Listonic shopping lists. Typical flow: get_lists → get_items → add_items / edit_items / delete_items.
            - Items are either unchecked (still to buy) or checked (bought / ticked off). "I bought X" = tick it off with edit_items, not delete.
            - Before adding, check the list for the same item; update its amount or untick it instead of adding a duplicate.
            - Keep item names in the user's language, product name only; quantity and unit go in their own fields.
            - Batch: one add_items / edit_items / delete_items call per list, with all items.
            - Not supported: creating or deleting lists, moving items between lists, categories, notes on items. Say so instead of searching for another tool.
            """;
    })
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
