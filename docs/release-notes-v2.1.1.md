ServicePilot 2.1.1 修正 AI 提示语（`ai-help` 和托盘右键菜单「复制给 AI 的帮助」），强调通过托盘管道修改配置后**即时生效、无需重启**，减少 AI 猜测和误操作。

## 亮点

- `AiHelpContentService` 中英文 AI 提示语增加「所有配置变更通过托盘管道后即时生效，无需重启托盘实例」的显式说明，消除 AI 在运行中托盘实例下总是先 `stop` 再启动的冗余行为。
- 版本属性更新为 `2.1.1`。

## 下载

- `ServicePilot.exe`

## 要求

- Windows
- 使用发布页自包含 `ServicePilot.exe` 时不需要单独安装 .NET 运行时。

## 安全说明

- ServicePilot 会执行用户配置的本地脚本；AI 帮助内容会包含本机 exe 绝对路径，只建议发送给可信个人 AI 助手，不要公开贴到网页、Issue 或日志。
- 自动化测试请设置 `SERVICEPILOT_CONFIG_DIR`，避免误改真实 `%APPDATA%\ServicePilot` 配置。

## 发布前检查

- [x] `dotnet build ServicePilot.sln`
- [x] `ServicePilot.exe ai-help` 和 `ServicePilot.exe doctor --json` 通过
- [x] 运行中托盘实例下验证 CLI 配置变更后 UI 即时刷新
- [x] GitHub Release 上传 `ServicePilot.exe`