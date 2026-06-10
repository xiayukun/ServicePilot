# Process Runner Research

[中文](process-runner-research.md)

This note records process-manager and command-runner projects reviewed while hardening ServicePilot. Keep it updated when ServicePilot adopts or rejects behavior inspired by these tools.

## Reviewed Projects

- [PM2](https://pm2.io/docs/runtime/reference/pm2-cli/): daemon-backed process manager with `start`, `stop`, `restart`, `list`, `logs`, and JSON-friendly automation commands.
- [Supervisor](https://www.supervisord.org/subprocess.html): explicit process states such as `STARTING`, `RUNNING`, `STOPPING`, `EXITED`, and `FATAL`; useful model for avoiding vague lifecycle transitions.
- [Foreman](https://ddollar.github.io/foreman/): Procfile runner with one command per process type and a clear shutdown timeout.
- [Honcho](https://honcho.readthedocs.io/en/latest/using_procfiles.html): Procfile runner that names process instances and injects useful environment variables.
- [Overmind](https://github.com/DarthSim/overmind): Procfile manager with per-process restart/connect behavior and strong local-development ergonomics.
- [concurrently](https://github.com/open-cli-tools/concurrently): cross-platform multi-command runner with prefixed output and kill-others policies.
- [npm-run-all](https://github.com/mysticatea/npm-run-all): sequential and parallel command orchestration with Windows-friendly npm-script usage.
- [mprocs](https://github.com/pvolok/mprocs): TUI runner with separate process output, per-process start/stop/restart, config files, and remote control.
- [nodemon](https://github.com/remy/nodemon): restart-on-change runner that distinguishes crashed apps from waiting/restart states.
- [watchexec](https://github.com/watchexec/watchexec): cross-platform command watcher with process groups, restart mode, filtering, and debounce behavior.
- [entr](https://github.com/eradman/entr): small file-change command runner with persistent-process restart behavior.
- [Task](https://taskfile.dev/): cross-platform YAML task runner that favors explicit task definitions over ad hoc shell chains.
- [just](https://github.com/casey/just): command runner focused on readable recipes, shell selection, and discoverable task invocation.

## Lessons For ServicePilot

- Lifecycle states must be explicit. ServicePilot should keep `Starting`, `Running`, `Stopping`, `Stopped`, `Completed`, `Error`, and `StartFailed` distinct.
- A service should leave `Starting` as soon as its final execution step has been reached. If the final step exits successfully it can become `Completed`; if it exits unsuccessfully it becomes `StartFailed`.
- Long-running steps that exit by themselves are startup/runtime failures for local dev services and should be visible as `StartFailed`.
- Stop should operate on the process tree and only warn when a real failure remains. Console commands launched without windows should not depend on `CloseMainWindow`.
- Logs need prefixes, stable ordering, bounded memory, and direct action affordances. A log view should expose start, stop, and restart for the selected service.
- CLI control should cover tray-visible operations and return machine-readable output where useful.
- Templates and config files should reduce repeated setup, but commands must remain explicit and reviewable.
- Future restart/watch/autorestart features should include debounce, restart policies, and clear "expected exit" versus "unexpected exit" semantics.
