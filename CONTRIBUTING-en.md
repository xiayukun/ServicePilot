# Contributing

中文：[CONTRIBUTING.md](CONTRIBUTING.md)

Thanks for taking a look at ServicePilot.

## Development Setup

Requirements:

- Windows
- .NET SDK 8.0+

Build:

```powershell
dotnet build .\ServicePilot\ServicePilot.csproj -c Release
```

## Pull Request Guidelines

- Keep process management safe: always use process tree killing for stop operations.
- Do not introduce command injection or unvalidated user input in script execution.
- Keep config keys and runtime file names in English.
- Update `README.md` when behavior changes.
- Verify with `dotnet build -c Release`.

## Safety Rules

Script execution runs user-provided commands through the configured script engine. The application should never execute arbitrary commands outside the user's configured steps.
