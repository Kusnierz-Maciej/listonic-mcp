using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Polly;
using Refit;

namespace ListonicApiClient;

public static class ServiceCollectionExtensions
{
    /// <summary>Named <see cref="HttpClient"/> used for the login call (no auth handler, same rate limit).</summary>
    public const string LoginClientName = "ListonicLogin";

    private const string RateLimitPipeline = "listonic-rate-limit";

    /// <param name="services">The service collection.</param>
    /// <param name="configureClient">
    /// Optional extension point: lets a caller (e.g. the diagnostic sandbox) append extra handlers to both
    /// the API and the login pipelines. Handlers added here land AFTER the built-in ones — i.e. innermost,
    /// closest to the socket — so a dump handler sees the fully-decorated request and fires once per
    /// physical send, including the auth handler's retry re-send.
    /// </param>
    public static IServiceCollection AddListonicApiClient(
        this IServiceCollection services,
        Action<IHttpClientBuilder>? configureClient = null)
    {
        services.AddSingleton<ListonicTokenStore>();
        services.TryAddSingleton<ListonicRateLimiter>();
        services.AddTransient<ListonicAuthHandler>();
        services.TryAddTransient<IListonicLoginValidator, ListonicLoginValidator>();

        var api = services
            .AddRefitClient<IListonicApi>()
            .ConfigureHttpClient(c => c.BaseAddress = new Uri(ListonicClientOptions.BaseUrl))
            .AddHttpMessageHandler<ListonicAuthHandler>();
        AddRateLimit(api);
        configureClient?.Invoke(api);

        var login = services
            .AddHttpClient(LoginClientName, c => c.BaseAddress = new Uri(ListonicClientOptions.BaseUrl));
        AddRateLimit(login);
        configureClient?.Invoke(login);

        return services;
    }

    // Outermost handler, so a request waits for a token before auth/login work happens.
    private static void AddRateLimit(IHttpClientBuilder builder) =>
        builder.AddResilienceHandler(RateLimitPipeline, (pipeline, context) =>
            pipeline.AddRateLimiter(context.ServiceProvider.GetRequiredService<ListonicRateLimiter>().Limiter));
}
