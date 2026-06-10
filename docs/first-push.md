# 首次推送检查清单

[English](first-push-en.md)

创建名为 `ServicePilot` 的空 GitHub 仓库后，使用这份清单。

## GitHub 仓库设置

创建仓库时使用以下选项：

- 拥有者：你的 GitHub 账号
- 仓库名：`ServicePilot`
- 描述：`ServicePilot | AI-friendly Windows tray service manager / 本地开发服务启动器：manage npm, Vite, dotnet, Python, PowerShell scripts with GUI + CLI.`
- 可见性：`Public`
- 添加 README：关闭
- 添加 `.gitignore`：`No .gitignore`
- 添加许可证：`No license`

本地仓库已经包含 `README.md`、`.gitignore` 和 `LICENSE`，GitHub 不需要生成这些文件。

## 本地 Git 设置

```powershell
git config user.name "xiayukun"
git config user.email "你的_GITHUB_邮箱"
```

## 首次提交

```powershell
git status --short
git add .
git commit -m "Initial public release"
```

## 连接远程

```powershell
git remote add origin https://github.com/xiayukun/ServicePilot.git
git branch -M main
git push -u origin main
```

## 推送后

- 确认 README 在 GitHub 上正确渲染。
- 确认构建工作流在 GitHub Actions 中启动，并通过 `ai-help` / `doctor --json` 冒烟测试。
- 按 `docs/repository-profile.md` 添加仓库主题标签。
- 确认 Issue 模板、PR 模板和 Dependabot 在 GitHub 页面可见。
- 创建首个发布。
