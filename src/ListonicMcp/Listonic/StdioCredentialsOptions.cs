using Microsoft.Extensions.Options;

namespace ListonicMcp.Listonic;

/// <summary>Binds the <c>Listonic</c> section — the single account used in stdio mode.</summary>
public sealed class StdioCredentialsOptions
{
    public const string Section = "Listonic";

    public string? Email { get; set; }
    public string? Password { get; set; }
}

/// <summary>Stdio mode needs both credentials up front — fail at start, not on the first tool call.</summary>
public sealed class StdioCredentialsOptionsValidator : IValidateOptions<StdioCredentialsOptions>
{
    public ValidateOptionsResult Validate(string? name, StdioCredentialsOptions options)
    {
        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.Email))
            failures.Add("Listonic:Email is required in stdio mode. Set the Listonic__Email environment variable.");
        if (string.IsNullOrWhiteSpace(options.Password))
            failures.Add("Listonic:Password is required in stdio mode. Set the Listonic__Password environment variable.");
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
