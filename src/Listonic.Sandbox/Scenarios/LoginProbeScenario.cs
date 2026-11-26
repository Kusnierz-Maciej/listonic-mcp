using Listonic.Sandbox.Dumping;
using ListonicApiClient;
using ListonicApiClient.Models.Login;
using Microsoft.Extensions.DependencyInjection;
using Refit;

namespace Listonic.Sandbox.Scenarios;

/// <summary>
/// WHAT: Login probe — three logins on the dumped login client: the real credentials, the real email
/// with a wrong password, and an unknown email. Touches no data.
///
/// WHY: the login outcome classifier (bad credentials vs API error) and the OAuth sign-in form need
/// to know exactly which status code and body Listonic returns for a credential rejection.
///
/// FOUND (2026-11-27): wrong password and unknown email both → HTTP 400
/// {"Message":"Unauthorized token. Token is invalid"}; a valid login → expires_in 86399 s.
/// Run sparingly — repeated failed logins could trip account protection.
/// </summary>
public sealed class LoginProbeScenario : IScenario
{
    public string Name => "login-probe";
    public string Description => "Logins: valid, wrong password, unknown email (no data touched).";

    public async Task RunAsync(IServiceProvider services, DumpRun run, string[] args, CancellationToken ct)
    {
        var options = services.GetRequiredService<ListonicClientOptions>();
        var factory = services.GetRequiredService<IHttpClientFactory>();
        var api = RestService.For<IListonicApi>(factory.CreateClient(ServiceCollectionExtensions.LoginClientName));

        await TryAsync(run, "valid", () => api.LoginAsync(
            new LoginRequest { Username = options.Email, Password = options.Password }, ct));
        await TryAsync(run, "wrong-password", () => api.LoginAsync(
            new LoginRequest { Username = options.Email, Password = options.Password + "-wrong" }, ct));
        await TryAsync(run, "unknown-email", () => api.LoginAsync(
            new LoginRequest { Username = $"nobody-{Guid.NewGuid():N}@example.invalid", Password = "x" }, ct));
    }

    private static async Task TryAsync(DumpRun run, string label, Func<Task<LoginResponse>> login)
    {
        try
        {
            var response = await login();
            run.Log($"{label}: OK token_type={response.TokenType} expires_in={response.ExpiresIn}s " +
                    $"access_token={(string.IsNullOrEmpty(response.AccessToken) ? "EMPTY" : "present")}");
        }
        catch (ApiException ex)
        {
            run.Log($"{label}: HTTP {(int)ex.StatusCode} {ex.StatusCode} body={ex.Content}");
        }
    }
}
