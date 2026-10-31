using System.Net;
using System.Net.Http.Headers;
using ListonicApiClient.Models.Login;
using Refit;

namespace ListonicApiClient;

public class ListonicAuthHandler : DelegatingHandler
{
    private readonly ListonicClientOptions _options;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _token;

    public ListonicAuthHandler(ListonicClientOptions options)
    {
        _options = options;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await GetTokenAsync(cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            await _lock.WaitAsync(cancellationToken);
            try { _token = null; }
            finally { _lock.Release(); }

            token = await GetTokenAsync(cancellationToken);

            var retryRequest = await CloneRequestAsync(request);
            retryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            response = await base.SendAsync(retryRequest, cancellationToken);
        }

        return response;
    }

    private async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        if (_token is not null)
            return _token;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_token is not null)
                return _token;

            _token = await LoginAsync(cancellationToken);
            return _token;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<string> LoginAsync(CancellationToken cancellationToken)
    {
        var loginApi = RestService.For<IListonicApi>(
            new HttpClient { BaseAddress = new Uri("https://api.listonic.com/api") });

        var response = await loginApi.LoginAsync(new LoginRequest
        {
            Username = _options.Email,
            Password = _options.Password
        }, cancellationToken);

        return response.AccessToken;
    }

    private static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage original)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri);

        if (original.Content is not null)
        {
            var bytes = await original.Content.ReadAsByteArrayAsync();
            clone.Content = new ByteArrayContent(bytes);
            foreach (var header in original.Content.Headers)
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        foreach (var header in original.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

        return clone;
    }
}
