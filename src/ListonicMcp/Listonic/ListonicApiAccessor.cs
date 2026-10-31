using ListonicApiClient;
using ListonicMcp.Security;

namespace ListonicMcp.Listonic;

public class ListonicApiAccessor(IHttpContextAccessor accessor, ListonicApiCache cache)
{
    public IListonicApi Api => cache.GetOrCreate(
        accessor.HttpContext!.Items[ApiKeyMiddleware.EmailItemKey] as string
            ?? throw new InvalidOperationException("Listonic email not resolved for this request."),
        accessor.HttpContext!.Items[ApiKeyMiddleware.PasswordItemKey] as string
            ?? throw new InvalidOperationException("Listonic password not resolved for this request."));
}
