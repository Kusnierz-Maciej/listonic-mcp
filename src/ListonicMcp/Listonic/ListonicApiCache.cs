using ListonicApiClient;
using Microsoft.Extensions.Caching.Memory;
using Refit;

namespace ListonicMcp.Listonic;

public class ListonicApiCache(IMemoryCache cache)
{
    private const string BaseUrl = "https://api.listonic.com/api";

    public IListonicApi GetOrCreate(string email, string password) =>
        cache.GetOrCreate((email, password), entry =>
        {
            entry.SlidingExpiration = TimeSpan.FromHours(1);
            return BuildClient(email, password);
        })!;

    private static IListonicApi BuildClient(string email, string password)
    {
        var options = new ListonicClientOptions { Email = email, Password = password };
        var handler = new ListonicAuthHandler(options);
        handler.InnerHandler = new HttpClientHandler();
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri(BaseUrl) };
        return RestService.For<IListonicApi>(httpClient);
    }
}
