using ListonicApiClient.Models.Login;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ListonicApiClient.Tests;

internal static class ApiClientFactory
{
    private sealed class Marker;

    private static readonly Lazy<(IListonicApi Api, ListonicClientOptions Options)> _instance = new(Build);

    public static IListonicApi Api => _instance.Value.Api;
    public static ListonicClientOptions Options => _instance.Value.Options;

    private static (IListonicApi, ListonicClientOptions) Build()
    {
        var config = new ConfigurationBuilder()
            .AddUserSecrets<Marker>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        var email    = config["Listonic:Email"]    ?? throw new InvalidOperationException("Listonic:Email not configured. Set via user-secrets or LISTONIC__EMAIL env var.");
        var password = config["Listonic:Password"] ?? throw new InvalidOperationException("Listonic:Password not configured. Set via user-secrets or LISTONIC__PASSWORD env var.");

        var options = new ListonicClientOptions { Email = email, Password = password };
        var services = new ServiceCollection();
        services.AddSingleton<IListonicCredentialProvider>(new StaticCredentialProvider(options));
        services.AddListonicApiClient();
        return (services.BuildServiceProvider().GetRequiredService<IListonicApi>(), options);
    }

    private sealed class StaticCredentialProvider(ListonicClientOptions options) : IListonicCredentialProvider
    {
        public ListonicClientOptions GetCredentials() => options;
    }

    public static LoginRequest CreateLoginRequest() => new()
    {
        Username = Options.Email,
        Password = Options.Password
    };
}
