using ListonicApiClient;
using Microsoft.Extensions.Options;

namespace ListonicMcp.Listonic;

/// <summary>Stdio mode: credentials come from configuration (Listonic__Email / Listonic__Password), validated at startup.</summary>
public class ConfigurationCredentialProvider(IOptions<StdioCredentialsOptions> options) : IListonicCredentialProvider
{
    public ListonicClientOptions GetCredentials() => new()
    {
        Email = options.Value.Email!,
        Password = options.Value.Password!,
    };
}
