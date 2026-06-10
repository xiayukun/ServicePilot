namespace ServicePilot.Models;

public enum ProcessState
{
    Stopped,
    Starting,
    Running,
    Stopping,
    Error,
    StartFailed,
    Completed
}

public enum StepRunState
{
    NotRun,
    Running,
    Succeeded,
    Failed,
    Skipped,
    Cancelled
}

public class StepRuntimeState
{
    public Guid StepId { get; set; }
    public string StepName { get; set; } = string.Empty;
    public StepRunState State { get; set; } = StepRunState.NotRun;
    public int? ExitCode { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public string? ActiveVariable { get; set; }
    public string? Error { get; set; }
}

public class ServiceRuntimeState
{
    public ServiceConfig Config { get; set; } = null!;
    public ProcessState State { get; set; } = ProcessState.Stopped;
    public int? ExitCode { get; set; }
    public DateTime? StartTime { get; set; }
    public string? LastError { get; set; }
    public string? ActiveVariable { get; set; }
    public Dictionary<Guid, StepRuntimeState> StepStates { get; set; } = new();
}
