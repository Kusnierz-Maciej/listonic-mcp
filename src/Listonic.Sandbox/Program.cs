using Listonic.Sandbox;
using Listonic.Sandbox.Dumping;
using Listonic.Sandbox.Scenarios;

// ─────────────────────────────────────────────────────────────────────────────────────────────
// Listonic.Sandbox — a private, local-only harness for driving the Listonic API / MCP tools with
// FULL request/response dumping, for diagnosis. Not shipped, not a test project.
//
// Usage:
//   dotnet run --project src/Listonic.Sandbox                          → list scenarios
//   dotnet run --project src/Listonic.Sandbox -- dump-lists            → run a scenario
//   dotnet run --project src/Listonic.Sandbox -- dump-items <id|name>  → scenario with arguments
//
// Every run writes to src/Listonic.Sandbox/out/<UTC-timestamp>/ : one NNNN_*.http file per HTTP
// exchange (full headers + body, request and response) plus run.log and meta.txt.
//
// SAFETY: this is the user's real account. Keep probing read-only; scenarios that write only touch
// lists they create themselves (prefixed "[test] ") and delete them afterwards.
// ─────────────────────────────────────────────────────────────────────────────────────────────

var scenarioName = args.FirstOrDefault();

if (string.IsNullOrWhiteSpace(scenarioName))
{
    Console.WriteLine("Available scenarios:");
    foreach (var s in ScenarioRegistry.All)
        Console.WriteLine($"  {s.Name,-20} {s.Description}");
    Console.WriteLine();
    Console.WriteLine("Run one with:  dotnet run --project src/Listonic.Sandbox -- <name> [args]");
    return 0;
}

var scenario = ScenarioRegistry.Find(scenarioName);
if (scenario is null)
{
    Console.Error.WriteLine($"Unknown scenario '{scenarioName}'. Known: {string.Join(", ", ScenarioRegistry.All.Select(s => s.Name))}");
    return 2;
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var run = new DumpRun(scenario.Name);
await using var services = SandboxHost.Build(run);

run.Log($"scenario '{scenario.Name}' starting…");
try
{
    await scenario.RunAsync(services, run, args[1..], cts.Token);
    run.Log($"scenario '{scenario.Name}' finished OK.");
}
catch (Exception ex)
{
    run.Log($"scenario '{scenario.Name}' FAILED: {ex}");
    Console.Error.WriteLine($"\nScenario failed — see the dump for the wire traffic. {ex.Message}");
    Console.WriteLine($"\nDump directory: {run.Directory}");
    return 1;
}

Console.WriteLine($"\nDump directory: {run.Directory}");
return 0;
