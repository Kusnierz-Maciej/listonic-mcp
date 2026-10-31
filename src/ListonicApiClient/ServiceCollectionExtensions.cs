using Microsoft.Extensions.DependencyInjection;
using Refit;

namespace ListonicApiClient;

public static class ServiceCollectionExtensions
{
    private const string BaseUrl = "https://api.listonic.com/api";

    public static IServiceCollection AddListonicApiClient(
        this IServiceCollection services,
        ListonicClientOptions options)
    {
        services.AddSingleton(options);
        services.AddTransient<ListonicAuthHandler>();

        services
            .AddRefitClient<IListonicApi>()
            .ConfigureHttpClient(c => c.BaseAddress = new Uri(BaseUrl))
            .AddHttpMessageHandler<ListonicAuthHandler>();

        return services;
    }

    public static IServiceCollection AddListonicApiClient(
        this IServiceCollection services,
        Action<HttpClient> configureClient)
    {
        services
            .AddRefitClient<IListonicApi>()
            .ConfigureHttpClient(configureClient)
            .AddHttpMessageHandler<ListonicAuthHandler>();

        return services;
    }
}
