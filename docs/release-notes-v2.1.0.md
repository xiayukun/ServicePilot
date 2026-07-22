ServicePilot 2.1.0 重点提升 AI 接手本地服务管理的可发现性，并补齐 CLI 修改配置后的运行中 UI 刷新。

## 亮点

- 托盘右键菜单新增 `复制给 AI 的帮助`，打开可复制窗口，显示当前 `ServicePilot.exe` 绝对路径、建议首批命令和可直接发给 AI 的提示词。
- 新增 `AiHelpContentService`，让 `ServicePilot.exe ai-help` 与托盘 AI 帮助窗口共用同源内容，减少 README、CLI 和 UI 指南漂移。
- 通过运行中托盘实例执行 CLI 配置变更后，会按命令类型刷新托盘菜单、服务管理窗口、模板管理窗口和相关日志窗口，无需重启应用才能看到配置变化。
- GitHub README、用户指南、AI 使用说明和截图指南改为优先引导用户下载启动后从托盘复制 AI 帮助，再让 AI 读取真实状态并创建个性化服务/模板/动作/变量。
- 版本属性更新为 `2.1.0`。

## 下载

- `ServicePilot.exe`

## 要求

- Windows
- 使用发布页自包含 `ServicePilot.exe` 时不需要单独安装 .NET 运行时。

## 安全说明

- ServicePilot 会执行用户配置的本地脚本；新增的 AI 帮助内容会包含本机 exe 绝对路径，只建议发送给可信个人 AI 助手，不要公开贴到网页、Issue 或日志。
- 自动化测试请设置 `SERVICEPILOT_CONFIG_DIR`，避免误改真实 `%APPDATA%\\ServicePilot` 配置。

## 发布前检查

- [ ] `rtk dotnet build ServicePilot.sln`
- [ ] 临时配置目录下 `ServicePilot.exe ai-help` 和 `ServicePilot.exe doctor --json` 通过
- [ ] 运行中托盘实例下验证 CLI `service add/edit/remove`、`step variable-*`、`template add/edit/remove/import/apply/save-from-service` 后 UI 即时刷新
- [ ] 托盘 AI 帮助窗口可选中文本，也可用 `复制全部` / `复制命令`
- [ ] GitHub Release 上传 `ServicePilot.exe`
