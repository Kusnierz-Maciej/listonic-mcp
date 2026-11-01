using ListonicApiClient;

namespace ListonicMcp.Listonic;

/// <summary>Stdio mode: credentials come from configuration (Listonic__Email / Listonic__Password).</summary>
public class ConfigurationCredentialProvider(IConfiguration configuration) : IListonicCredentialProvider
{
    public ListonicClientOptions GetCredentials()
    {
        var email = configuration["Listonic:Email"];
        var password = configuration["Listonic:Password"];

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException(
                "Listonic credentials are required. Set Listonic__Email and Listonic__Password.");

        return new ListonicClientOptions { Email = email, Password = password };
    }
}
