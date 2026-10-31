using Refit;

namespace ListonicMcpApiClient.Models.Lists;

public class GetListsRequest
{
    [AliasAs("includeShares")]
    public bool IncludeShares { get; init; }

    [AliasAs("archive")]
    public bool Archive { get; init; }

    [AliasAs("includeItems")]
    public bool IncludeItems { get; init; }
}
