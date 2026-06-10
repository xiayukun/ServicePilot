# Competitive Code Research

[中文](competitive-research.md)

This document records related projects reviewed before ServicePilot's public launch. Sources include public GitHub repositories, official docs, and local source clones. The goal is not to copy features, but to decide what ServicePilot should learn from, avoid, and use as positioning.

## Summary

ServicePilot should keep these directions:

- Focus on Windows local development, not production daemons.
- Provide both tray GUI and AI-friendly CLI.
- Keep service configuration persistent, explicit, and reviewable.
- Keep logs bounded, searchable, copyable, and safe for long output.
- Stop process groups reliably, especially npm/Vite child processes.
- Avoid complex DAGs in the first version. Keep the model as service + ordered steps + variables + templates.

## Compared Projects

| Project | Type | Good ideas | ServicePilot decision |
| --- | --- | --- | --- |
| [PM2](https://github.com/Unitech/pm2) | Node.js process manager | daemon, JSON status, logs, environment overlays | Keep JSON status and env ideas; skip production clustering |
| [concurrently](https://github.com/open-cli-tools/concurrently) | multi-command runner | output prefixes, success conditions, kill-others policies | Keep clearer output/failure semantics; avoid one-shot-only positioning |
| [npm-run-all](https://github.com/mysticatea/npm-run-all) | npm scripts orchestration | sequential/parallel flows, Windows process-tree handling | Keep ordered steps and reliable Windows stopping |
| [Foreman](https://github.com/ddollar/foreman) | Procfile runner | `.env`, Procfile, formation, shutdown timeout | Do not use Procfile as the main config; possible future import |
| [Overmind](https://github.com/DarthSim/overmind) | Procfile + tmux manager | long-lived supervisor, per-process restart/connect, socket commands | Keep CLI-to-running-instance control; skip tmux model |
| [Hivemind](https://github.com/DarthSim/hivemind) | simple Procfile manager | env configuration, port stepping, process-group signals | Do not auto-assign ports; keep process-group cleanup |
| [Goreman](https://github.com/mattn/goreman) | Go Procfile manager | RPC control, Windows CTRL_BREAK, partial-line log buffering | Keep command pipe; consider gentler stop phase later |
| [Task](https://github.com/go-task/task) | YAML task runner | variables, includes, dependencies, cross-platform tasks | Avoid full task DSL; keep variables as simple strings |
| [just](https://github.com/casey/just) | command runner | discoverable recipes, completions, human-friendly commands | Strengthen `ai-help` and command discovery |
| [WinSW](https://github.com/winsw/winsw) | Windows service wrapper | Windows service lifecycle, log rolling, hooks | Do not install OS services; borrow log rotation later |
| [Servy](https://github.com/aelassas/servy) | Windows service GUI/CLI | GUI + CLI, logs, health checks, recovery policies | Differentiate with tray-first UX and AI CLI |
| [Listr2](https://github.com/listr2/listr2) | task-list state machine | detailed task states, renderers, concurrency/failure control | Current step states are enough; retry/rollback can come later |

## Code-Level Notes

### PM2

PM2's source focuses on process daemons, environment merging, log paths, JSON config, and machine-readable status such as list/describe output. It is powerful for production operations, but strongly tied to the Node.js ecosystem.

ServicePilot keeps JSON status and variable injection ideas, but intentionally skips clustering, load balancing, and production ecosystem files.

### concurrently

concurrently separates command definitions, output streams, prefixes, success conditions, and cancellation signals. It is a one-shot command runner, not a persistent service registry.

ServicePilot keeps the lesson that stderr is not automatically failure, and that output should remain readable, copyable, and bounded.

### npm-run-all

npm-run-all handles Windows process-tree termination and distinguishes sequential and parallel npm script orchestration.

ServicePilot keeps ordered startup steps and Windows process-group cleanup, but persists services and templates beyond one command invocation.

### Foreman / Overmind / Hivemind / Goreman

Procfile-family tools emphasize declaring multiple processes, controlling a single process at runtime, prefixed logs, and graceful shutdown with timeout.

ServicePilot keeps CLI control of the running tray instance, single-service/single-step operations, and robust stop fallbacks. It avoids Procfile as the main config, tmux attach/connect, and automatic port assignment.

### Task / just

Task and just make project commands discoverable and reusable inside a repository.

ServicePilot differs by centralizing multiple local folders, exposing tray/log-window actions, and making AI interaction explicit through `ai-help` and JSON queries.

### WinSW / Servy

WinSW and Servy are closer to Windows service management and production-style recovery. They are useful references for log rotation, hooks, health checks, and restart policies.

ServicePilot stays lighter: no OS service installation, no admin requirement, and optimized for frequent local dev start/stop cycles.

### Listr2

Listr2 is useful for clear task states. ServicePilot already has `NotRun`, `Running`, `Succeeded`, `Failed`, `Skipped`, and `Cancelled`. Retry, rollback, and richer summaries can come later.

## Changes Driven By This Research

This round keeps or adds:

- README positioning around AI-operated local development services.
- `ServicePilot.exe ai-help`.
- Service step-variable CLI maintenance: `step variables`, `step variable-add`, `step variable-remove`, `step variable-clear`.
- Template step-variable CLI maintenance: `template step-variables`, `template step-variable-add`, `template step-variable-remove`, `template step-variable-clear`.
- Async end-to-end config saves in CLI paths, avoiding command-mode hangs and leftover processes.
- Added `doctor [--json]` configuration diagnostics, borrowing health-check/diagnosability ideas while keeping the scan local.
- Initial-release changelog wording instead of pre-launch implementation history.

## Roadmap

- Import/export configuration.
- Scan `package.json`, Taskfile, justfile, and Procfile for candidate services.
- Optional restart policies.
- Log export and lightweight rotation.
- Health checks.
- Stricter JSON schema docs for AI agents.
