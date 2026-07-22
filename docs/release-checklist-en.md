# Release Checklist

中文：[release-checklist.md](release-checklist.md)

## Build

Exit any running `dist\ServicePilot.exe` before publishing to `dist`. Use `dist-staged` when you only need to validate the publish command.

```powershell
dotnet publish .\ServicePilot\ServicePilot.csproj -t:Rebuild -c Release -o .\dist
```

The `Release` configuration in `ServicePilot.csproj` defaults to `win-x64`, self-contained, compressed single-file publish; `dist` should contain only `ServicePilot.exe`.

Expected artifact:

```text
dist\ServicePilot.exe
```

## Local Verification

- Run from a temporary config directory:
  - `ServicePilot.exe ai-help`
  - `ServicePilot.exe doctor --json`
- Double-click `ServicePilot.exe`.
- Confirm tray icon appears.
- Confirm the tray context menu `Language` can switch between follow-system, Chinese, and English.
- Confirm the tray context menu `Copy help for AI` opens its window and can copy all content or commands.
- Add a test service and start it.
- Confirm log output appears.
- Stop and restart the service.
- Run one CLI configuration change through the running tray instance and confirm the tray menu plus open manager/log windows refresh immediately.
- Run `ServicePilot.exe shutdown` and confirm no child processes remain.

## GitHub Release

- Tag: `vX.Y.Z` (e.g. `v4.0.0`).
- Title: `ServicePilot X.Y.Z` (an optional short subtitle is fine, e.g. `ServicePilot 4.0.0 — Log merge & folding`).
- Artifact: upload `ServicePilot.exe` to the release **Assets**.
- Use the Chinese release notes in `docs/release-notes-vX.Y.Z.md`; follow the body format in `docs/release-notes-template.md`.
- Update `CHANGELOG.md` and `CHANGELOG-en.md` before publishing.
- Confirm `.github/workflows/build.yml` passes for the tag or manual run and uploads `ServicePilot.exe`.
- Update Chinese and English screenshots using `docs/screenshot-guide.md`.

### Release notes convention

- We only maintain Chinese release notes (`docs/release-notes-vX.Y.Z.md`) plus the Chinese/English `CHANGELOG`. Per-version English release notes (`docs/release-notes-vX.Y.Z-en.md`) are deprecated and removed.
- The GitHub Release body must not repeat a `# ServicePilot X.Y.Z` heading and must not include a "Download" section — downloads go through the Assets.
- End the body with a single English entry that links only to the changelog:

  ```text
  ---

  🌐 English: [Changelog](https://github.com/xiayukun/ServicePilot/blob/main/CHANGELOG-en.md)
  ```
