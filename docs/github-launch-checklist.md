# GitHub 上线检查清单

[English](github-launch-checklist-en.md)

这份清单把 ServicePilot 从本地工具变成更容易被信任、试用和收藏的仓库。

## 定位

用最广泛的有用承诺来引导：

> 从 Windows 托盘和 CLI 运行、监控并自动化多个本地开发服务。

推荐的 GitHub 主题标签：

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

## 发布前

- 按 `docs/screenshot-guide.md` 准备中英文真实截图；README 首屏主图当前使用 `Assets/screenshots/tray-menu-zh.png`，社交预览图需要时再重新生成。
- 确认发布制品名为 `ServicePilot.exe`。
- 确认 README 的下载、`复制给 AI 的帮助`、快速开始和 CLI 章节符合当前应用。
- 确认 `LICENSE` 存在。
- 确认 `PRIVACY.md` 存在，并解释仅本地运行。
- 按 `docs/first-push.md` 创建空 GitHub 仓库并首次推送。
- 确认 `.github/workflows/build.yml` 在 GitHub Actions 中通过，并且发布后的 `ai-help` / `doctor --json` 冒烟测试成功。
- 确认 Issue 模板、PR 模板、Dependabot 和 `.gitignore` 都符合当前托盘优先、AI CLI 友好的定位。
- 用简短更新日志创建首个发布。
- 按 `docs/repository-profile.md` 添加仓库配置。

## 热门仓库通常做得好的方面

清晰的首屏：

- README 用一两句话解释问题。
- 主要截图出现在靠近顶部的位置。
- 安装和快速开始说明一眼可见。

低试用成本：

- 用户可以下载一个制品。
- 第一次点击显而易见：下载启动后，右键托盘数字并复制给 AI 的帮助；需要命令行时再看 `ai-help` / `doctor --json`。
- 失败模式和局限性有文档说明。

信任信号：

- 许可证存在。
- 构建工作流可见。
- Issue 有模板。
- PR 模板提醒托盘、日志窗口、CLI/AI 命令和安全边界。
- 依赖更新配置存在。
- 发布有命名和版本。

社区准备：

- `CONTRIBUTING.md` 存在。
- 安全报告路径存在。
- 路线图具体但不臃肿。
