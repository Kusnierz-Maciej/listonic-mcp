namespace Listonic.Sandbox.Scenarios;

/// <summary>Central list of runnable scenarios, keyed by <see cref="IScenario.Name"/>.</summary>
public static class ScenarioRegistry
{
    public static readonly IReadOnlyList<IScenario> All =
    [
        new DumpListsScenario(),
        new DumpItemsScenario(),
        new DumpCategoriesScenario(),
        new ToolsSmokeScenario(),
        new LoginProbeScenario(),
        new RoundtripScenario(),
    ];

    public static IScenario? Find(string name) =>
        All.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
}
