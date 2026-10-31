using ListonicApiClient;
using ListonicMcp.Security;

namespace ListonicMcp.Listonic;

public class HttpContextCredentialProvider(IHttpContextAccessor accessor) : IListonicCredentialProvider
{
    public ListonicClientOptions GetCredentials()
    {
        var context = accessor.HttpContext
            ?? throw new InvalidOperationException("No active HTTP context for the Listonic request.");

        var email = context.Items[ApiKeyMiddleware.EmailItemKey] as string
            ?? throw new InvalidOperationException("Listonic email not resolved for this request.");
        var password = context.Items[ApiKeyMiddleware.PasswordItemKey] as string
            ?? throw new InvalidOperationException("Listonic password not resolved for this request.");

        return new ListonicClientOptions { Email = email, Password = password };
    }
}
