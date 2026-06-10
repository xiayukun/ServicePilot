namespace ServicePilot.Services;

public class CommandResponse
{
    public int ExitCode { get; set; }
    public string Output { get; set; } = string.Empty;
    public bool IsError { get; set; }

    public static CommandResponse Ok(string output) => new()
    {
        ExitCode = 0,
        Output = output
    };

    public static CommandResponse Error(string output, int exitCode = 1) => new()
    {
        ExitCode = exitCode,
        Output = output,
        IsError = true
    };
}
