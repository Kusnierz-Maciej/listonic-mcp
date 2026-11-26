using Listonic.Sandbox.Dumping;
using ListonicApiClient;
using Microsoft.Extensions.DependencyInjection;

namespace Listonic.Sandbox.Scenarios;

/// <summary>
/// WHAT: READ-ONLY. Dumps <c>/categoriesv2</c> (the product categories items are classified into).
///
/// WHY: groundwork for a future categories feature (plan item S9b) — the response is not modelled
/// yet (<c>object</c>), so the dump is the reference for designing the model and tool.
///
/// FOUND / USE IT FOR: category ids/names, localisation, how items' CategoryId maps onto them.
/// </summary>
public sealed class DumpCategoriesScenario : IScenario
{
    public string Name => "dump-categories";
    public string Description => "Read-only: raw /categoriesv2 payload.";

    public async Task RunAsync(IServiceProvider services, DumpRun run, string[] args, CancellationToken ct)
    {
        var api = services.GetRequiredService<IListonicApi>();
        await api.GetCategoriesAsync(ct);
        run.Log("categories dumped — see the .http file for the payload.");
    }
}
