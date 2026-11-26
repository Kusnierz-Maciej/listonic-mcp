using Listonic.Sandbox.Dumping;

namespace Listonic.Sandbox.Scenarios;

/// <summary>
/// A named thing to run against the live API/MCP tools with full request/response dumping.
/// Add new probes by implementing this and registering them in <see cref="ScenarioRegistry"/>.
/// </summary>
public interface IScenario
{
    /// <summary>CLI name, e.g. <c>dump-lists</c>.</summary>
    string Name { get; }

    /// <summary>One-line description shown when listing scenarios.</summary>
    string Description { get; }

    /// <param name="args">Command-line arguments after the scenario name.</param>
    Task RunAsync(IServiceProvider services, DumpRun run, string[] args, CancellationToken ct);
}
