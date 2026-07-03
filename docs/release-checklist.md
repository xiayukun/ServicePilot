# 发布检查清单

English: [release-checklist-en.md](release-checklist-en.md)

## 构建

发布到 `dist` 前先退出正在运行的 `dist\ServicePilot.exe`。如果只是验证发布命令，可以输出到 `dist-staged`。

```powershell
rtk dotnet publish .\ServicePilot\ServicePilot.csproj -t:Rebuild -c Release -o .\dist
```

`ServicePilot.csproj` 的 `Release` 配置默认使用 `win-x64`、自包含、单文件、压缩发布；`dist` 应只保留 `ServicePilot.exe`。

预期制品：

```text
dist\ServicePilot.exe
```

## 本地验证

- 在临时配置目录运行：
  - `ServicePilot.exe ai-help`
  - `ServicePilot.exe doctor --json`
- 双击 `ServicePilot.exe`。
- 确认托盘图标出现。
- 确认托盘右键菜单的 `语言` 可在跟随系统、中文、English 之间切换。
- 确认托盘右键菜单的 `复制给 AI 的帮助` 可打开窗口，并可复制全部内容或复制命令。
- 添加测试服务并启动。
- 确认日志输出出现。
- 停止并重启服务。
- 通过运行中的托盘实例执行一次 CLI 配置变更，确认托盘菜单和已打开的管理/日志窗口即时刷新。
- 运行 `ServicePilot.exe shutdown`，确认退出不会残留子进程。

## GitHub 发布

- 标签：`v2.1.0`
- 标题：`ServicePilot 2.1.0`
- 制品：`ServicePilot.exe`
- 使用 `docs/release-notes-v2.1.0.md` 的发布说明。
- 发布前更新 `CHANGELOG.md` 和 `CHANGELOG-en.md`。
- 确认 `.github/workflows/build.yml` 在标签或手动触发时通过，并上传 `ServicePilot.exe`。
- 按 `docs/screenshot-guide.md` 更新中文和英文截图。
