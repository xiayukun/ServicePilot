# GitHub Launch Checklist

[中文](github-launch-checklist.md)

This checklist turns ServicePilot from a local utility into a repository that is easier to trust, try, and star.

## Positioning

Lead with the broadest useful promise:

> Run, monitor, and automate multiple local dev services from the Windows tray and CLI.

Good GitHub topics:

- `windows`
- `task-runner`
- `service-manager`
- `system-tray`
- `developer-tools`
- `dotnet`
- `wpf`
- `cli`
- `automation`
- `process-manager`
- `devops`

## Before Publishing

- Prepare Chinese and English screenshots with `docs/screenshot-guide.md`; the README hero currently uses `Assets/screenshots/tray-menu-zh.png`, and the social preview image should be regenerated only when needed.
- Confirm the release artifact is named `ServicePilot.exe`.
- Confirm README download, `Copy help for AI`, quick start, and CLI sections match the current app.
- Confirm `LICENSE` exists.
- Confirm `PRIVACY.md` exists and explains local-only behavior.
- Follow `docs/first-push.md` for the empty GitHub repository and first push.
- Confirm `.github/workflows/build.yml` passes on GitHub Actions, including the published `ai-help` / `doctor --json` smoke test.
- Confirm issue templates, PR templates, Dependabot, and `.gitignore` match the current tray-first, AI CLI-friendly positioning.
- Create the first release with a short changelog.
- Add the repository profile from `docs/repository-profile.md`.

## What Popular Repositories Usually Do Well

Clear first screen:

- The README explains the problem in one or two sentences.
- The primary screenshot appears near the top.
- Install and quick-start instructions are visible without hunting.

Low trial cost:

- Users can download one artifact.
- The first click is obvious: after download and launch, right-click the tray number and copy help for AI; command-line users can then check `ai-help` / `doctor --json`.
- Failure modes and limitations are documented.

Trust signals:

- License is present.
- Build workflow is visible.
- Issues have templates.
- The PR template reminds maintainers to check tray behavior, log windows, CLI/AI commands, and safety boundaries.
- Dependency update configuration exists.
- Releases are named and versioned.

Community readiness:

- `CONTRIBUTING.md` exists.
- Security reporting path exists.
- The roadmap is specific but not bloated.
