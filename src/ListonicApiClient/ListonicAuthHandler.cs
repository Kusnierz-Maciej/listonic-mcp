using System.Net;
using System.Net.Http.Headers;
using ListonicApiClient.Models.Login;
using Refit;

namespace ListonicApiClient;

public class ListonicAuthHandler : DelegatingHandler
{
    private readonly IListonicCredentialProvider _credentials;
    private readonly ListonicTokenStore _tokens;
    private readonly IHttpClientFactory _httpClientFactory;

    public ListonicAuthHandler(
        IListonicCredentialProvider credentials,
        ListonicTokenStore tokens,
        IHttpClientFactory httpClientFactory)
    {
        _credentials = credentials;
        _tokens = tokens;
        _httpClientFactory = httpClientFactory;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var credentials = _credentials.GetCredentials();

        var token = await _tokens.GetOrLoginAsync(credentials, ct => LoginAsync(credentials, ct), cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            _tokens.Invalidate(credentials, token);

            token = await _tokens.GetOrLoginAsync(credentials, ct => LoginAsync(credentials, ct), cancellationToken);

            var retryRequest = await CloneRequestAsync(request);
            retryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            response = await base.SendAsync(retryRequest, cancellationToken);
        }

        return response;
    }

    private async Task<string> LoginAsync(ListonicClientOptions credentials, CancellationToken cancellationToken)
    {
        // Factory client: pooled handlers (no socket exhaustion) and the shared rate limit.
        var loginApi = RestService.For<IListonicApi>(
            _httpClientFactory.CreateClient(ServiceCollectionExtensions.LoginClientName));

        var response = await loginApi.LoginAsync(new LoginRequest
        {
            Username = credentials.Email,
            Password = credentials.Password
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
