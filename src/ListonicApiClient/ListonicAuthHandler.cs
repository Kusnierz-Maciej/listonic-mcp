using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;

namespace ListonicApiClient;

public class ListonicAuthHandler : DelegatingHandler
{
    private readonly IListonicCredentialProvider _credentials;
    private readonly ListonicTokenStore _tokens;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ListonicAuthHandler>? _logger;

    public ListonicAuthHandler(
        IListonicCredentialProvider credentials,
        ListonicTokenStore tokens,
        IHttpClientFactory httpClientFactory,
        ILogger<ListonicAuthHandler>? logger = null)
    {
        _credentials = credentials;
        _tokens = tokens;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var credentials = _credentials.GetCredentials();

        var token = await _tokens.GetOrLoginAsync(credentials, ct => LoginAsync(credentials, ct), cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            _logger?.LogWarning("Listonic returned HTTP 401; invalidating the cached token and re-logging in once.");
            _tokens.Invalidate(credentials, token);

            token = await _tokens.GetOrLoginAsync(credentials, ct => LoginAsync(credentials, ct), cancellationToken);

            var retryRequest = await CloneRequestAsync(request);
            retryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            response.Dispose();
            response = await base.SendAsync(retryRequest, cancellationToken);

            // Still 401 with a token we just obtained: the credentials themselves are no longer accepted.
            // Surface that distinctly so the MCP layer can force re-authentication.
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                _logger?.LogError("Listonic still returned HTTP 401 after a fresh login — credentials rejected.");
                _tokens.Invalidate(credentials, token);
                response.Dispose();
                throw new ListonicCredentialsRejectedException("HTTP 401 after re-login");
            }
        }

        return response;
    }

    private async Task<string> LoginAsync(ListonicClientOptions credentials, CancellationToken cancellationToken)
    {
        var outcome = await ListonicLogin.TryLoginAsync(
            _httpClientFactory, credentials.Email, credentials.Password, _logger, cancellationToken);

        return outcome.Kind switch
        {
            LoginOutcomeKind.Ok => outcome.Token!,
            LoginOutcomeKind.BadCredentials => throw new ListonicCredentialsRejectedException(outcome.Detail),
            _ => throw new HttpRequestException(
                $"Listonic login failed with an API error ({outcome.Detail}). Try again shortly."),
        };
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
