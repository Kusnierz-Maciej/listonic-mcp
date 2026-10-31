using ListonicApiClient;

namespace ListonicMcp.Listonic;

public class ListonicApiAccessor(IListonicApi api)
{
    public IListonicApi Api => api;
}
