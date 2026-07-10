中文发布说明：当前文件 | 英文：[release-notes-v2.2.0-en.md](release-notes-v2.2.0-en.md)

ServicePilot 2.2.0 新增**动作级增量编辑 CLI**：AI 和脚本现在可以精确地对单个步骤执行增、改、删、排序操作，无需重新创建整个服务。

## 亮点

- **`step add`**：在服务中新增动作，支持指定位置（`--position end|N|after:STEP|before:STEP`）和直接加入组合动作（`--into-composite COMPOSITE`）。
- **`step edit`**：修改动作的名称、脚本类型、脚本内容、变量开关和日志弹出行为。
- **`step remove`**：删除指定动作，自动清理组合动作中的成员引用。
- **`step move`**：移动动作到新位置，支持序号、`after:STEP`、`before:STEP`。
- **`AiHelpContentService`** 和 `ai-help` CLI 同步新增上述命令示例，AI 友好性进一步提升。
- 通过运行中托盘管道执行 `step add/edit/remove/move` 后，托盘菜单和已打开的管理/日志窗口即时刷新。

## 下载

- `ServicePilot.exe`

## 要求

- Windows
- 使用发布页自包含 `ServicePilot.exe` 时不需要单独安装 .NET 运行时。

## 安全说明

- ServicePilot 会执行用户配置的本地脚本；AI 帮助内容会包含本机 exe 绝对路径，只建议发送给可信个人 AI 助手，不要公开贴到网页、Issue 或日志。
- 自动化测试请设置 `SERVICEPILOT_CONFIG_DIR`，避免误改真实 `%APPDATA%\ServicePilot` 配置。

## 发布前检查

- [ ] `dotnet build ServicePilot.sln`
- [ ] `ServicePilot.exe ai-help` 和 `ServicePilot.exe doctor --json` 通过
- [ ] 运行中托盘实例下验证 `step add/edit/remove/move` 后 UI 即时刷新
- [ ] GitHub Release 上传 `ServicePilot.exe`