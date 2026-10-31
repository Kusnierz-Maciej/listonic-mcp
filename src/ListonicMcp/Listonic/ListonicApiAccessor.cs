using ListonicApiClient;

namespace ListonicMcp.Listonic;

public class ListonicApiAccessor(IHttpContextAccessor accessor, ListonicApiCache cache)
{
    public IListonicApi Api => cache.GetOrCreate(
        accessor.HttpContext!.Items["listonic-email"] as string
            ?? throw new InvalidOperationException("Listonic email not resolved for this request."),
        accessor.HttpContext!.Items["listonic-password"] as string
            ?? throw new InvalidOperationException("Listonic password not resolved for this request."));
}
