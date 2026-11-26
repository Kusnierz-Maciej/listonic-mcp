using Listonic.Sandbox.Dumping;
using ListonicApiClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Listonic.Sandbox;

/// <summary>
/// Builds the sandbox DI container: the real Listonic API client (same handler chain the app uses,
/// rate limit included) plus the innermost <see cref="DumpingHttpHandler"/> on both the API and the
/// login pipelines. Credentials come from user secrets (Listonic:Email / Listonic:Password) or env vars
/// — the same store the live tests use.
/// </summary>
public static class SandboxHost
{
    public static ServiceProvider Build(DumpRun run)
    {
        var config = new ConfigurationBuilder()
            .AddUserSecrets(typeof(SandboxHost).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build();

        var options = new ListonicClientOptions
        {
            Email = config["Listonic:Email"]
                ?? throw new InvalidOperationException("Listonic:Email not set (user secret or LISTONIC__EMAIL env var)."),
            Password = config["Listonic:Password"]
                ?? throw new InvalidOperationException("Listonic:Password not set (user secret or LISTONIC__PASSWORD env var)."),
        };

        var services = new ServiceCollection();
        services.AddLogging();

        // The dump run + handler, appended INNERMOST via the configureClient hook so it sees the
        // fully decorated request and fires once per physical send.
        services.AddSingleton(run);
        services.AddSingleton(options);
        services.AddTransient<DumpingHttpHandler>();

        services.AddSingleton<IListonicCredentialProvider>(new StaticCredentialProvider(options));
        services.AddListonicApiClient(client => client.AddHttpMessageHandler<DumpingHttpHandler>());

        return services.BuildServiceProvider();
    }

    private sealed class StaticCredentialProvider(ListonicClientOptions options) : IListonicCredentialProvider
    {
        public ListonicClientOptions GetCredentials() => options;
    }
}
