ServicePilot 3.0.0 带来 **FluentWindow 现代界面全面升级**：所有管理窗口迁移为 WPF-UI FluentWindow，支持 TitleBar 标题栏、系统主题色选中效果和统一的深色主题。

## 亮点

- **FluentWindow 界面升级**：AI 帮助、日志、服务管理/编辑、模板管理/编辑、模板选择、变量输入等所有窗口全面迁移为 WPF-UI `FluentWindow`，启用 `ExtendsContentIntoTitleBar` 现代标题栏，支持最小化/最大化/关闭按钮。
- **深色主题统一**：托盘菜单新增系统主题色 hover 高亮 + 左侧 3px 选中条；列表项选中时显示系统主题色边框；所有文字控件统一深色前景色。
- **日志窗口工具栏重构**：按钮样式升级为 WPF-UI Primary/Danger 外观，布局采用一致间距和圆角。
- **AI 帮助窗口迁移**：加入 TitleBar、深色背景适配，移除旧版标题栏。
- **ServiceCommandProcessor 增强**：新增 CLI 模板操作和扩展能力。
- **ServiceManager UI 对齐**：列表项选中样式、按钮布局全面对齐 FluentWindow 设计系统。

## 下载

- `ServicePilot.exe`

## 要求

- Windows
- 使用发布页自包含 `ServicePilot.exe` 时不需要单独安装 .NET 运行时。

## 安全说明

- ServicePilot 会执行用户配置的本地脚本；AI 帮助内容会包含本机 exe 绝对路径，只建议发送给可信个人 AI 助手，不要公开贴到网页、Issue 或日志。
- 自动化测试请设置 `SERVICEPILOT_CONFIG_DIR`，避免误改真实 `%APPDATA%\\ServicePilot` 配置。
