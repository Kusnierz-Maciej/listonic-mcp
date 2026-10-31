using Microsoft.Extensions.DependencyInjection;
using Refit;

namespace ListonicApiClient;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddListonicApiClient(this IServiceCollection services)
    {
        services.AddSingleton<ListonicTokenStore>();
        services.AddTransient<ListonicAuthHandler>();

        services
            .AddRefitClient<IListonicApi>()
            .ConfigureHttpClient(c => c.BaseAddress = new Uri(ListonicClientOptions.BaseUrl))
            .AddHttpMessageHandler<ListonicAuthHandler>();

        return services;
    }
}
