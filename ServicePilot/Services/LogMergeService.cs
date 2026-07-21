using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ServicePilot.Models;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.Loader;

namespace ServicePilot.Services;

/// <summary>
/// Compiles, caches, and executes C# log merge functions.
///
/// Uses the low-level <see cref="CSharpCompilation"/> API (emit to memory + collectible
/// <see cref="AssemblyLoadContext"/>) instead of the Roslyn Scripting API. The Scripting API
/// hard-codes a file-path lookup for corlib/host assemblies (see dotnet/roslyn#50719), which
/// throws <c>NotSupportedException</c> under single-file publish where <c>Assembly.Location</c>
/// is empty. The compilation API lets us supply every reference from in-memory raw metadata,
/// so it works in single-file bundles.
///
/// Thread-safe. Compile failures are cached by script text so repeated bad scripts are fast-pathed.
/// </summary>
public sealed class LogMergeService : IDisposable
{
    private const string GeneratedTypeName = "ServicePilot.Generated.MergeScriptRunner";
    private const string GeneratedMethodName = "Run";

    private sealed record CompiledScript(AssemblyLoadContext Context, Func<MergeScriptGlobals, MergeResult?> Invoke);

    private readonly ConcurrentDictionary<string, CompiledScript> _cache = new();
    private readonly ConcurrentDictionary<string, string> _errorCache = new();
    private readonly ConcurrentDictionary<string, bool> _slowScripts = new();
    private readonly SemaphoreSlim _compileLock = new(1, 1);
    private readonly TimeSpan _executionTimeout;
    private readonly Lazy<MetadataReference[]> _references;
    private bool _disposed;

    public LogMergeService(TimeSpan? executionTimeout = null)
    {
        _executionTimeout = executionTimeout ?? TimeSpan.FromMilliseconds(500);
        _references = new Lazy<MetadataReference[]>(BuildReferences);
    }

    private static string NormalizeKey(string script) => script.Trim();

    // ---- Public API ----

    /// <summary>Returns true if the script has been compiled (or errored) already.</summary>
    public bool IsKnown(string script)
    {
        var key = NormalizeKey(script);
        return _cache.ContainsKey(key) || _errorCache.ContainsKey(key);
    }

    /// <summary>Returns the cached compile error for a script, or null if it compiled OK.</summary>
    public string? GetCompileError(string script)
    {
        var key = NormalizeKey(script);
        return _errorCache.GetValueOrDefault(key);
    }

    /// <summary>
    /// Compiles a C# merge script and caches the result.
    /// Returns true on success, false on compile error (call <see cref="GetCompileError"/> for details).
    /// </summary>
    public async Task<bool> CompileAsync(string script, CancellationToken ct = default)
    {
        var key = NormalizeKey(script);

        if (_cache.ContainsKey(key)) return true;
        if (_errorCache.ContainsKey(key)) return false;

        await _compileLock.WaitAsync(ct);
        try
        {
            if (_cache.ContainsKey(key)) return true;
            if (_errorCache.ContainsKey(key)) return false;

            return TryCompileInternal(key, script, ct);
        }
        finally
        {
            _compileLock.Release();
        }
    }

    /// <summary>
    /// Evaluates a compiled merge script against the given globals.
    /// Returns a <see cref="MergeResult"/> when the script returns one, or null on failure/timeout.
    /// Automatically compiles the script first if not already cached.
    /// </summary>
    public async Task<MergeResult?> EvaluateAsync(string script, MergeScriptGlobals globals, CancellationToken ct = default)
    {
        var key = NormalizeKey(script);

        if (!_cache.TryGetValue(key, out var compiled))
        {
            // ConfigureAwait(false): never resume on a captured (UI) SynchronizationContext. Callers
            // that block on this task from the UI thread would otherwise deadlock.
            var ok = await CompileAsync(script, ct).ConfigureAwait(false);
            if (!ok || !_cache.TryGetValue(key, out compiled))
                return null;
        }

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_executionTimeout);

            // The user function is synchronous; run it on a worker so a runaway loop can be abandoned.
            var invoke = compiled.Invoke;
            var task = Task.Run(() => invoke(globals), timeoutCts.Token);
            var completed = await Task.WhenAny(task, Task.Delay(_executionTimeout, timeoutCts.Token)).ConfigureAwait(false);
            if (completed != task)
                return null; // timed out — abandon (task keeps running but result is ignored)

            return await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception)
        {
            // Any runtime error in user script must not bubble up to the caller.
            return null;
        }
    }

    /// <summary>
    /// Synchronous evaluation for the UI/log-render hot path. The log window needs a result per line
    /// immediately, so it must NOT block on an async method that captures the UI SynchronizationContext
    /// (that deadlocks). This runs the compiled function on a worker thread with a hard timeout and
    /// never touches the calling thread's context.
    /// Returns null on compile failure, timeout, or runtime error.
    /// </summary>
    public MergeResult? Evaluate(string script, MergeScriptGlobals globals)
    {
        var key = NormalizeKey(script);

        if (!_cache.TryGetValue(key, out var compiled))
        {
            // Compile synchronously off the caller's context. If it fails, the error is cached and
            // GetCompileError can surface it.
            if (_errorCache.ContainsKey(key))
                return null;
            _compileLock.Wait();
            try
            {
                if (!_cache.TryGetValue(key, out compiled))
                {
                    if (_errorCache.ContainsKey(key))
                        return null;
                    if (!TryCompileInternal(key, script, CancellationToken.None) ||
                        !_cache.TryGetValue(key, out compiled))
                        return null;
                }
            }
            finally
            {
                _compileLock.Release();
            }
        }

        try
        {
            var invoke = compiled.Invoke;

            // Fast path: run inline (no thread-pool hop). This is the hot path for high-frequency
            // output (e.g. webpack progress bursts) where spawning a Task per line would thrash the
            // pool and stall the UI thread. A well-behaved regex script returns in microseconds.
            if (!_slowScripts.ContainsKey(key))
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var inlineResult = invoke(globals);
                sw.Stop();
                // If a single invocation is unexpectedly slow, mark the script so future calls run on
                // an isolated worker with a hard timeout (protects the UI from a runaway/slow script).
                if (sw.Elapsed > _executionTimeout)
                    _slowScripts[key] = true;
                return inlineResult;
            }

            // Isolated path for scripts that proved slow: abandon after the timeout so the UI thread
            // is never blocked longer than _executionTimeout.
            var task = Task.Run(() => invoke(globals));
            if (!task.Wait(_executionTimeout))
                return null; // timed out — abandon (worker keeps running but result is ignored)
            return task.Result;
        }
        catch (Exception)
        {
            // Any runtime error in user script must not bubble up to the caller.
            return null;
        }
    }

    /// <summary>
    /// Removes a script from both caches. Unloads its collectible assembly context.
    /// Useful when the user edits a previously-broken script and wants to retry.
    /// </summary>
    public void Invalidate(string script)
    {
        var key = NormalizeKey(script);
        if (_cache.TryRemove(key, out var compiled))
            TryUnload(compiled.Context);
        _errorCache.TryRemove(key, out _);
        _slowScripts.TryRemove(key, out _);
    }

    /// <summary>Clears all cached scripts and errors, unloading their assembly contexts.</summary>
    public void Clear()
    {
        foreach (var compiled in _cache.Values)
            TryUnload(compiled.Context);
        _cache.Clear();
        _errorCache.Clear();
        _slowScripts.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Clear();
        _compileLock.Dispose();
    }

    // ---- Internals ----

    private bool TryCompileInternal(string key, string script, CancellationToken ct)
    {
        try
        {
            var source = BuildSource(script);
            var tree = CSharpSyntaxTree.ParseText(source, cancellationToken: ct);
            var compilation = CSharpCompilation.Create(
                "MergeScriptAsm_" + Guid.NewGuid().ToString("N"),
                new[] { tree },
                _references.Value,
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel: OptimizationLevel.Release,
                    allowUnsafe: false,
                    warningLevel: 0));

            using var ms = new MemoryStream();
            var emit = compilation.Emit(ms, cancellationToken: ct);
            if (!emit.Success)
            {
                var errors = emit.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(FormatDiagnostic);
                _errorCache[key] = string.Join(Environment.NewLine, errors);
                return false;
            }

            ms.Seek(0, SeekOrigin.Begin);
            var context = new AssemblyLoadContext("MergeScript_" + Guid.NewGuid().ToString("N"), isCollectible: true);
            var assembly = context.LoadFromStream(ms);
            var type = assembly.GetType(GeneratedTypeName);
            var method = type?.GetMethod(GeneratedMethodName, BindingFlags.Public | BindingFlags.Static);
            if (method == null)
            {
                TryUnload(context);
                _errorCache[key] = "内部错误：找不到生成的合并方法。";
                return false;
            }

            var del = (Func<MergeScriptGlobals, MergeResult?>)Delegate.CreateDelegate(
                typeof(Func<MergeScriptGlobals, MergeResult?>), method);

            _cache[key] = new CompiledScript(context, del);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _errorCache[key] = ex.Message;
            return false;
        }
    }

    private static string FormatDiagnostic(Diagnostic d)
    {
        // Translate the generated-source line back to the user's script line (offset by the wrapper prefix).
        var line = d.Location.GetLineSpan().StartLinePosition.Line - UserBodyStartLine + 1;
        if (line < 1)
            return d.GetMessage();
        return $"第 {line} 行: {d.GetMessage()}";
    }

    // 0-based line index at which the user's body begins in BuildSource (see the layout below).
    // Must stay in sync with the number of prefix lines emitted by BuildSource.
    private const int UserBodyStartLine = 19;

    private static string BuildSource(string userBody)
    {
        // The user writes a method body that ends with a `return <MergeResult? expr>;`.
        // We inject the globals as locals so scripts read them naturally.
        return $$"""
                 using System;
                 using System.Linq;
                 using System.Collections.Generic;
                 using System.Text;
                 using System.Text.RegularExpressions;
                 using System.Globalization;
                 using ServicePilot.Models;

                 namespace ServicePilot.Generated
                 {
                     public static class MergeScriptRunner
                     {
                         public static MergeResult? Run(MergeScriptGlobals __globals)
                         {
                             string? PreviousLine = __globals.PreviousLine;
                             string? CurrentLine = __globals.CurrentLine;
                             MergeResult? PreviousResult = __globals.PreviousResult;
                             bool PreviousWasCollapsed = __globals.PreviousWasCollapsed;
                             bool InCollapseGroup = __globals.InCollapseGroup;
                 {{userBody}}
                         }
                     }
                 }
                 """;
    }

    private MetadataReference[] BuildReferences()
    {
        var assemblies = new[]
        {
            typeof(object).Assembly,                                 // System.Private.CoreLib
            typeof(Enumerable).Assembly,                             // System.Linq
            typeof(System.Collections.Generic.List<>).Assembly,
            typeof(System.Text.StringBuilder).Assembly,
            typeof(System.Text.RegularExpressions.Regex).Assembly,   // System.Text.RegularExpressions
            typeof(System.Uri).Assembly,                             // System.Private.Uri
            typeof(System.Globalization.CultureInfo).Assembly,
            typeof(MergeResult).Assembly,                            // ServicePilot (MergeResult / MergeScriptGlobals)
            SafeLoad("System.Runtime"),
            SafeLoad("System.Text.RegularExpressions"),
            SafeLoad("System.Collections"),
            SafeLoad("netstandard")
        };

        var references = new List<MetadataReference>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var assembly in assemblies)
        {
            if (assembly == null)
                continue;

            var name = assembly.FullName ?? assembly.GetName().Name ?? string.Empty;
            if (!seen.Add(name))
                continue;

            var reference = TryCreateReference(assembly);
            if (reference != null)
                references.Add(reference);
        }

        return references.ToArray();
    }

    private static Assembly? SafeLoad(string name)
    {
        try { return Assembly.Load(name); }
        catch { return null; }
    }

    private static unsafe MetadataReference? TryCreateReference(Assembly assembly)
    {
        // Under single-file publish Assembly.Location is empty, so we read the in-memory
        // metadata image directly. This is the only path that works in a single-file bundle.
        try
        {
            if (assembly.TryGetRawMetadata(out var blob, out var length))
            {
                var moduleMetadata = ModuleMetadata.CreateFromMetadata((IntPtr)blob, length);
                return AssemblyMetadata.Create(moduleMetadata).GetReference();
            }
        }
        catch
        {
            // Skip assemblies whose raw metadata is unavailable.
        }

        return null;
    }

    private static void TryUnload(AssemblyLoadContext context)
    {
        try { context.Unload(); }
        catch { /* best-effort */ }
    }
}
