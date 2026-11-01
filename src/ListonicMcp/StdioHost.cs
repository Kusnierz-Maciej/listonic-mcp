using ListonicApiClient;
using ListonicMcp.Listonic;

namespace ListonicMcp;

/// <summary>
/// Stdio mode: credentials come from Listonic__Email / Listonic__Password.
/// </summary>
public static class StdioHost
{
    public static async Task RunAsync(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        // stdout carries the MCP protocol, so all logs go to stderr.
        builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

        builder.Services.AddTransient<IListonicCredentialProvider, ConfigurationCredentialProvider>();
        builder.Services.AddListonicApiClient();
        builder.Services.AddScoped<ListonicApiAccessor>();

        builder.Services
            .AddMcpServer(options => options.ServerInstructions = McpInstructions.Text)
            .WithStdioServerTransport()
            .WithToolsFromAssembly();

        await builder.Build().RunAsync();
    }
}
