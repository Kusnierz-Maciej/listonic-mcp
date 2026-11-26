using ListonicApiClient;
using ListonicMcp.Listonic;
using ListonicMcp.Tools;
using Microsoft.Extensions.Options;

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

        builder.Services.AddOptions<StdioCredentialsOptions>()
            .Bind(builder.Configuration.GetSection(StdioCredentialsOptions.Section))
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<StdioCredentialsOptions>, StdioCredentialsOptionsValidator>();
        builder.Services.AddTransient<IListonicCredentialProvider, ConfigurationCredentialProvider>();
        builder.Services.AddListonicApiClient();

        builder.Services
            .AddMcpServer(options => options.ServerInstructions = McpInstructions.Text)
            .WithStdioServerTransport()
            .WithTools<ListonicTools>();

        var host = builder.Build();
        if (!StartupValidation.TryValidate(host.Services))
            return;

        await host.RunAsync();
    }
}
