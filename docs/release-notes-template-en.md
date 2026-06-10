# Release Notes Template

中文模板：[release-notes-template.md](release-notes-template.md)

Copy this template into a GitHub release and edit it for the version being published.

## ServicePilot vX.Y.Z

Chinese release notes: `docs/release-notes-vX.Y.Z.md`

### Highlights

- Bullet point summary of changes.

### Download

- `ServicePilot.exe`

### Requirements

- Windows
- .NET 8.0 runtime, or use the self-contained executable.

### Safety Notes

- ServicePilot executes user-configured scripts.
- Review script content before running services.

### Checks

- [ ] `dotnet build .\ServicePilot\ServicePilot.csproj -c Release`
- [ ] `ServicePilot.exe ai-help` and `ServicePilot.exe doctor --json` pass under a temporary config directory
- [ ] Publish and test the single-file executable
- [ ] Screenshots updated
- [ ] GitHub release asset uploaded
