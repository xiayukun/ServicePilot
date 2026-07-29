using System.Collections.Concurrent;
using System.Diagnostics;
using ServicePilot.Models;
using ServicePilot.Services;

var tests = new (string Name, Func<Task> Run)[]
{
    ("cancel-publication-window", CancelPublicationWindowAsync),
    ("slow-subscriber-tail-and-nonzero", SlowSubscriberTailAndNonzeroAsync),
    ("drain-timeout-is-not-success-and-no-late-delivery", DrainTimeoutAsync),
    ("manager-drain-timeout-publishes-error-once", ManagerDrainTimeoutAsync),
    ("manager-stop-publishes-one-terminal-state", ManagerTerminalStateAsync)
};

var failures = new List<string>();
foreach (var test in tests)
{
    var stopwatch = Stopwatch.StartNew();
    try
    {
        await test.Run();
        Console.WriteLine($"PASS {test.Name} ({stopwatch.ElapsedMilliseconds} ms)");
    }
    catch (Exception ex)
    {
        failures.Add(test.Name);
        Console.WriteLine($"FAIL {test.Name}: {ex.GetType().Name}: {ex.Message}");
    }
}

if (failures.Count > 0)
{
    Console.Error.WriteLine($"FAILED {failures.Count}/{tests.Length}: {string.Join(", ", failures)}");
    return 1;
}

Console.WriteLine($"PASSED {tests.Length}/{tests.Length}");
return 0;

static async Task CancelPublicationWindowAsync()
{
    var directory = CreateTempDirectory();
    var marker = Path.Combine(directory, "started-after-stop.txt");
    var step = PowerShellStep("publication", $"Start-Sleep -Milliseconds 100; [IO.File]::WriteAllText('{EscapePowerShell(marker)}', 'started')");
    var config = new ServiceConfig
    {
        Name = "publication-race",
        WorkingDirectory = directory,
        ScriptSteps = [step]
    };
    using var cts = new CancellationTokenSource();
    using var executor = new ScriptExecutor(config, cts.Token, new ServiceStartOptions { OnlyStepId = step.Id });
    var headerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var releaseHeader = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    executor.OutputReceived += entry =>
    {
        if (!entry.Message.StartsWith("--- 动作", StringComparison.Ordinal))
            return;

        headerEntered.TrySetResult();
        releaseHeader.Task.GetAwaiter().GetResult();
    };

    var runTask = Task.Run(() => executor.RunSingleStepAsync(step.Id, null));
    await headerEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
    cts.Cancel();
    var stopTask = executor.StopAsync();
    await stopTask.WaitAsync(TimeSpan.FromSeconds(5));
    releaseHeader.TrySetResult();

    try
    {
        await runTask.WaitAsync(TimeSpan.FromSeconds(10));
    }
    catch (OperationCanceledException)
    {
    }

    await Task.Delay(300);
    Assert(!File.Exists(marker), "Stop returned while an unpublished runner could still start and execute.");
    Directory.Delete(directory, recursive: true);
}

static async Task SlowSubscriberTailAndNonzeroAsync()
{
    var directory = CreateTempDirectory();
    var step = PowerShellStep(
        "tails",
        "1..200 | ForEach-Object { [Console]::Out.WriteLine(\"out-$_\"); [Console]::Error.WriteLine(\"err-$_\") }; " +
        "[Console]::Out.Write('stdout-tail'); [Console]::Error.Write('stderr-tail'); exit 7");
    using var runner = new ProcessRunner(step, directory);
    var stdout = new ConcurrentBag<string>();
    var stderr = new ConcurrentBag<string>();
    runner.OutputReceived += entry =>
    {
        if (entry.Source == "stdout")
            stdout.Add(entry.Message);
        else if (entry.Source == "stderr")
            stderr.Add(entry.Message);
        Thread.Sleep(2);
    };

    runner.Start();
    var exitCode = await runner.Completion.WaitAsync(TimeSpan.FromSeconds(15));

    Assert(exitCode == 7, $"Expected exit 7, got {exitCode}.");
    Assert(stdout.Count == 201 && stdout.Contains("stdout-tail"), $"stdout drain incomplete: {stdout.Count} lines.");
    Assert(stderr.Count == 201 && stderr.Contains("stderr-tail"), $"stderr drain incomplete: {stderr.Count} lines.");
    Directory.Delete(directory, recursive: true);
}

static async Task DrainTimeoutAsync()
{
    var directory = CreateTempDirectory();
    var step = PowerShellStep("drain-timeout", "[Console]::Out.WriteLine('slow-ui'); Start-Sleep -Seconds 30");
    using var runner = new ProcessRunner(step, directory);
    var slowCallbackEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var callbackStarts = 0;
    runner.OutputReceived += entry =>
    {
        Interlocked.Increment(ref callbackStarts);
        if (entry.Message != "slow-ui")
            return;

        slowCallbackEntered.TrySetResult();
        Thread.Sleep(TimeSpan.FromSeconds(6));
    };

    runner.Start();
    await slowCallbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

    var timedOut = false;
    try
    {
        await runner.StopAsync().WaitAsync(TimeSpan.FromSeconds(12));
    }
    catch (TimeoutException)
    {
        timedOut = true;
    }

    Assert(timedOut, "A drain timeout was reported as a successful stop.");
    var startsAtReturn = Volatile.Read(ref callbackStarts);
    await Task.Delay(500);
    Assert(Volatile.Read(ref callbackStarts) == startsAtReturn, "Output delivery started after StopAsync returned.");
    Directory.Delete(directory, recursive: true);
}

static async Task ManagerTerminalStateAsync()
{
    var directory = CreateTempDirectory();
    var action = PowerShellStep("managed", "[Console]::Out.WriteLine('ready'); Start-Sleep -Seconds 30");
    var composite = new ScriptStep
    {
        Name = "main",
        Kind = StepKind.Composite,
        Order = 0,
        MemberStepIds = [action.Id]
    };
    action.Order = 1;
    var config = new ServiceConfig
    {
        Name = "manager-stop",
        WorkingDirectory = directory,
        ScriptSteps = [composite, action]
    };
    using var manager = new ProcessManager();
    manager.AddService(config);
    var running = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var terminalStates = new ConcurrentQueue<ProcessState>();
    manager.ServiceOutput += (_, entry) =>
    {
        if (entry.Message == "ready")
            running.TrySetResult();
    };
    manager.ServiceStateChanged += (_, state) =>
    {
        if (state is ProcessState.Stopped or ProcessState.Completed or ProcessState.StartFailed or ProcessState.Error)
            terminalStates.Enqueue(state);
    };

    Assert(manager.StartService(config.Id), "Manager refused to start test service.");
    await running.Task.WaitAsync(TimeSpan.FromSeconds(5));
    await manager.StopServiceAsync(config.Id).WaitAsync(TimeSpan.FromSeconds(10));
    await Task.Delay(250);

    Assert(terminalStates.Count == 1, $"Expected one terminal state, got {string.Join(", ", terminalStates)}.");
    Assert(terminalStates.TryPeek(out var terminal) && terminal == ProcessState.Stopped, $"Expected Stopped, got {terminal}.");
    Directory.Delete(directory, recursive: true);
}

static async Task ManagerDrainTimeoutAsync()
{
    var directory = CreateTempDirectory();
    var action = PowerShellStep("managed-timeout", "[Console]::Out.WriteLine('slow-manager-ui'); Start-Sleep -Seconds 30");
    var composite = new ScriptStep
    {
        Name = "main",
        Kind = StepKind.Composite,
        Order = 0,
        MemberStepIds = [action.Id]
    };
    action.Order = 1;
    var config = new ServiceConfig
    {
        Name = "manager-timeout",
        WorkingDirectory = directory,
        ScriptSteps = [composite, action]
    };
    using var manager = new ProcessManager();
    manager.AddService(config);
    var slowCallbackEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var terminalStates = new ConcurrentQueue<ProcessState>();
    manager.ServiceOutput += (_, entry) =>
    {
        if (entry.Message != "slow-manager-ui")
            return;
        slowCallbackEntered.TrySetResult();
        Thread.Sleep(TimeSpan.FromSeconds(6));
    };
    manager.ServiceStateChanged += (_, state) =>
    {
        if (state is ProcessState.Stopped or ProcessState.Completed or ProcessState.StartFailed or ProcessState.Error)
            terminalStates.Enqueue(state);
    };

    Assert(manager.StartService(config.Id), "Manager refused to start timeout service.");
    await slowCallbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
    var threw = false;
    try
    {
        await manager.StopServiceAsync(config.Id).WaitAsync(TimeSpan.FromSeconds(12));
    }
    catch (TimeoutException)
    {
        threw = true;
    }

    Assert(threw, "Manager swallowed the runner drain timeout.");
    await Task.Delay(250);
    Assert(terminalStates.Count == 1, $"Expected one timeout terminal state, got {string.Join(", ", terminalStates)}.");
    Assert(terminalStates.TryPeek(out var terminal) && terminal == ProcessState.Error, $"Expected Error, got {terminal}.");
    Directory.Delete(directory, recursive: true);
}

static ScriptStep PowerShellStep(string name, string content) => new()
{
    Name = name,
    Kind = StepKind.Action,
    ScriptType = ScriptType.PowerShell,
    Content = content,
    UseVariable = false
};

static string CreateTempDirectory()
{
    var directory = Path.Combine(Path.GetTempPath(), "ServicePilot-ConcurrencyHarness", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    return directory;
}

static string EscapePowerShell(string value) => value.Replace("'", "''", StringComparison.Ordinal);

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
