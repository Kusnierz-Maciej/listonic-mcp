using Microsoft.Extensions.Options;

namespace ListonicMcp;

/// <summary>
/// Runs every <c>ValidateOnStart</c> options check up front and turns failures into a readable
/// operator message on stderr plus exit code 1, instead of the host's stack-trace log.
/// </summary>
internal static class StartupValidation
{
    public static bool TryValidate(IServiceProvider services)
    {
        try
        {
            services.GetRequiredService<IStartupValidator>().Validate();
            return true;
        }
        catch (OptionsValidationException ex)
        {
            Report(ex.Failures);
        }
        catch (AggregateException ex) when (ex.InnerExceptions.All(e => e is OptionsValidationException))
        {
            Report(ex.InnerExceptions.Cast<OptionsValidationException>().SelectMany(e => e.Failures));
        }

        Environment.ExitCode = 1;
        return false;
    }

    private static void Report(IEnumerable<string> failures)
    {
        Console.Error.WriteLine("FATAL: invalid configuration — refusing to start:");
        foreach (var failure in failures)
            Console.Error.WriteLine($"  - {failure}");
    }
}
