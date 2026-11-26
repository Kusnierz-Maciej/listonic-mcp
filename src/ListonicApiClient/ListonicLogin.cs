using System.Net;
using ListonicApiClient.Models.Login;
using Microsoft.Extensions.Logging;
using Refit;

namespace ListonicApiClient;

/// <summary>
/// Shared Listonic login: one call on the rate-limited login client, classified into a
/// <see cref="LoginOutcome"/>. Used by <see cref="ListonicAuthHandler"/> (to get a bearer token) and the
/// OAuth <c>/authorize</c> validator, so "what counts as a bad-credential login" lives in one place.
/// </summary>
public static class ListonicLogin
{
    /// <summary>Attempts a login and classifies the outcome. Never throws for the expected failure cases.</summary>
    public static async Task<LoginOutcome> TryLoginAsync(
        IHttpClientFactory httpClientFactory, string email, string password, ILogger? logger, CancellationToken ct)
    {
        var api = RestService.For<IListonicApi>(
            httpClientFactory.CreateClient(ServiceCollectionExtensions.LoginClientName));

        try
        {
            var response = await api.LoginAsync(new LoginRequest { Username = email, Password = password }, ct);

            if (string.IsNullOrWhiteSpace(response.AccessToken))
            {
                logger?.LogInformation("Listonic login returned no access token; treating as rejected credentials.");
                return LoginOutcome.Bad("empty access token");
            }

            return LoginOutcome.Success(response.AccessToken);
        }
        catch (ApiException ex)
        {
            var outcome = Classify(ex.StatusCode, ex.Content);
            if (outcome.Kind == LoginOutcomeKind.BadCredentials)
                logger?.LogInformation("Listonic login rejected the credentials ({Detail}).", outcome.Detail);
            else
                logger?.LogWarning("Listonic login failed with an API error ({Detail}); not a credential failure.", outcome.Detail);
            return outcome;
        }
        catch (HttpRequestException ex)
        {
            logger?.LogWarning(ex, "Listonic login failed with a transport error; treating as an API error.");
            return LoginOutcome.Api("transport error");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            logger?.LogWarning("Listonic login timed out; treating as an API error.");
            return LoginOutcome.Api("timeout");
        }
    }

    /// <summary>
    /// Maps a failed login response to an outcome. The login endpoint is OAuth-style, so a credential
    /// rejection is 400 (<c>invalid_grant</c> / <c>invalid_request</c>), 401 or 403. Everything else —
    /// 429, 5xx, other 4xx — is an API problem, not a wrong password.
    /// PROVISIONAL until confirmed with the sandbox <c>login-probe</c> scenario (see docs/API-NOTES.md).
    /// </summary>
    public static LoginOutcome Classify(HttpStatusCode status, string? body)
    {
        var detail = $"HTTP {(int)status}";
        return status switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => LoginOutcome.Bad(detail),
            HttpStatusCode.BadRequest when body is null
                || body.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase)
                || body.Contains("invalid_request", StringComparison.OrdinalIgnoreCase)
                || body.Contains("password", StringComparison.OrdinalIgnoreCase)
                => LoginOutcome.Bad($"{detail} invalid_grant"),
            _ => LoginOutcome.Api(detail),
        };
    }
}

/// <summary>Default <see cref="IListonicLoginValidator"/> backed by <see cref="ListonicLogin"/>.</summary>
public sealed class ListonicLoginValidator(IHttpClientFactory httpClientFactory, ILogger<ListonicLoginValidator> logger)
    : IListonicLoginValidator
{
    public Task<LoginOutcome> ValidateAsync(string email, string password, CancellationToken ct = default) =>
        ListonicLogin.TryLoginAsync(httpClientFactory, email, password, logger, ct);
}
