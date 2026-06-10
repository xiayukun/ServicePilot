# First Push Checklist

[中文](first-push.md)

Use this checklist after creating an empty GitHub repository named `ServicePilot`.

## GitHub Repository Settings

Create the repository with these options:

- Owner: your GitHub account
- Repository name: `ServicePilot`
- Description: `ServicePilot | AI-friendly Windows tray service manager / 本地开发服务启动器：manage npm, Vite, dotnet, Python, PowerShell scripts with GUI + CLI.`
- Visibility: `Public`
- Add README: off
- Add `.gitignore`: `No .gitignore`
- Add license: `No license`

The local repository already contains `README.md`, `.gitignore`, and `LICENSE`, so GitHub should not generate those files.

## Local Git Settings

```powershell
git config user.name "xiayukun"
git config user.email "YOUR_GITHUB_EMAIL"
```

## First Commit

```powershell
git status --short
git add .
git commit -m "Initial public release"
```

## Connect Remote

```powershell
git remote add origin https://github.com/xiayukun/ServicePilot.git
git branch -M main
git push -u origin main
```

## After Push

- Confirm the README renders correctly on GitHub.
- Confirm the build workflow starts under GitHub Actions and passes the `ai-help` / `doctor --json` smoke test.
- Add the repository topics listed in `docs/repository-profile.md`.
- Confirm issue templates, PR templates, and Dependabot are visible on GitHub.
- Create the first release.
