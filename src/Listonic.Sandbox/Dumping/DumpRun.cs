using System.Diagnostics;

namespace Listonic.Sandbox.Dumping;

/// <summary>
/// Owns a single sandbox run's output: a per-run directory <c>out/&lt;yyyyMMddTHHmmssZ&gt;/</c>,
/// a monotonically increasing exchange sequence counter (so concurrent HTTP sends get distinct,
/// ordered filenames), and a free-form run log you can write diagnostic notes to.
///
/// FULL FIDELITY: dumps include live bearer tokens, the login password and the static client secret, unredacted. The
/// <c>out/</c> tree is gitignored and stays local — this is a private diagnostic tool.
/// </summary>
public sealed class DumpRun
{
    private int _sequence;
    private readonly string _logPath;

    public string Directory { get; }

    public DumpRun(string? scenarioName = null, string? baseDirOverride = null)
    {
        var baseDir = baseDirOverride
            ?? Environment.GetEnvironmentVariable("LISTONIC_SANDBOX_OUT")
            ?? ResolveDefaultOutDir();

        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'");
        Directory = Path.Combine(baseDir, stamp);
        System.IO.Directory.CreateDirectory(Directory);
        _logPath = Path.Combine(Directory, "run.log");

        File.WriteAllText(
            Path.Combine(Directory, "meta.txt"),
            $"started : {DateTimeOffset.UtcNow:u}{Environment.NewLine}" +
            $"machine : {Environment.MachineName}{Environment.NewLine}" +
            $"scenario: {scenarioName ?? "(none)"}{Environment.NewLine}");

        Log($"run directory: {Directory}");
    }

    /// <summary>Next 1-based exchange sequence number, used as the <c>NNNN</c> file prefix.</summary>
    public int NextSequence() => Interlocked.Increment(ref _sequence);

    /// <summary>Append a free-form note to run.log and echo it to the console.</summary>
    public void Log(string message)
    {
        var line = $"[{DateTimeOffset.UtcNow:HH:mm:ss.fff}] {message}";
        Console.WriteLine(line);
        lock (_logPath)
            File.AppendAllText(_logPath, line + Environment.NewLine);
    }

    /// <summary>
    /// Default output base: &lt;project&gt;/out. Walks up from the build output directory
    /// (bin/Debug/netX) to the project folder so dumps land beside the source, not under bin/.
    /// Falls back to the current directory if the project folder cannot be located.
    /// </summary>
    private static string ResolveDefaultOutDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (dir.GetFiles("Listonic.Sandbox.csproj").Length > 0)
                return Path.Combine(dir.FullName, "out");
            dir = dir.Parent;
        }
        return Path.Combine(Environment.CurrentDirectory, "out");
    }
}
