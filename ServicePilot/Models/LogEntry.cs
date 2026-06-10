namespace ServicePilot.Models;

public enum LogLevel
{
    Info,
    Warning,
    Error,
    System
}

public class LogEntry
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public LogLevel Level { get; set; } = LogLevel.Info;
    public string Message { get; set; } = string.Empty;
    public string Source { get; set; } = "stdout";
    public string? StepName { get; set; }

    public LogEntry() { }

    public LogEntry(LogLevel level, string message, string source, string? stepName = null)
    {
        Timestamp = DateTime.Now;
        Level = level;
        Message = message;
        Source = source;
        StepName = stepName;
    }
}
