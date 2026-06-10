# Release Checklist

中文：[release-checklist.md](release-checklist.md)

## Build

Exit any running `dist\ServicePilot.exe` before publishing to `dist`. Use `dist-staged` when you only need to validate the publish command.

```powershell
dotnet publish .\ServicePilot\ServicePilot.csproj -t:Rebuild -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o .\dist
```

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
- Add a test service and start it.
- Confirm log output appears.
- Stop and restart the service.
- Run `ServicePilot.exe shutdown` and confirm no child processes remain.

## GitHub Release

- Tag: `v1.0.0`
- Title: `ServicePilot 1.0.0`
- Artifact: `ServicePilot.exe`
- Use release notes from `docs/release-notes-v1.0.0.md`.
- Update `CHANGELOG.md` and `CHANGELOG-en.md` before publishing.
- Confirm `.github/workflows/build.yml` passes for the tag or manual run and uploads `ServicePilot.exe`.
- Update Chinese and English screenshots using `docs/screenshot-guide.md`.
