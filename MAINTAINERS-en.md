# Maintainers

中文：[MAINTAINERS.md](MAINTAINERS.md)

Primary maintainer:

- xiayukun

## Maintenance Principles

- Keep the first-run workflow simple.
- Keep process management safe and predictable.
- Prefer explicit user confirmation for destructive operations.
- Keep config keys and runtime file names in English.
- Keep Chinese UI text isolated in documentation files.

## Release Rhythm

Small bug-fix releases are preferred over large, risky batches. A release should include:

- a passing Windows build
- a fresh `ServicePilot.exe` artifact
- short release notes
- any README updates needed for changed behavior
