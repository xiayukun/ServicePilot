# 宣传截图指南

[English](screenshot-guide-en.md)

这份清单用于首个公开发布前准备 README、GitHub Release 和社交预览截图。

## 截图前准备

- 准备 4 到 6 个演示服务，名称建议使用 `screen`、`web`、`app`、`h5`、`order-web` 这类真实本地开发项目。
- 至少让 1 个服务处于运行中，1 个服务处于已停止，必要时保留 1 个失败示例用于排查能力截图。
- 给服务配置 2 到 3 个变量，例如本地、测试、开发 API 地址。
- 准备一个完整模板，包含修改配置步骤和 `npm run dev` 启动步骤。
- 截图前清理敏感路径、令牌、私有域名；需要展示域名时使用可公开的示例值。
- 分别切到中文和 English 各截一组关键截图。右键菜单路径：`语言` / `Language`。

## 必须截图

1. 托盘右键主菜单  
   展示数字托盘图标、服务列表、状态小点、启动/执行步骤/日志/编辑/模板操作，以及语言切换菜单。

2. 管理服务窗口  
   展示多服务列表、运行状态、步骤数量、变量数量，以及启动、执行步骤、停止、重启、日志等操作按钮。

3. 新增或编辑服务窗口  
   展示服务名称、工作目录、多步骤脚本、`使用变量`、`启动执行`、预设变量/手动步骤变量，以及应用模板按钮。

4. 日志窗口  
   展示实时日志、搜索框、复制菜单、横向滚动长行，以及启动/执行步骤/停止/重启/编辑按钮。

5. 管理模板窗口  
   展示完整服务模板列表、说明、步骤数量、变量数量和模板预览。

6. CLI / AI 使用截图  
   在终端展示：

   ```powershell
   ServicePilot.exe ai-help
   ServicePilot.exe doctor --json
   ServicePilot.exe status all --json
   ```

## 当前已整理截图

- `Assets/app-preview.png`：README 主图和 GitHub 预览图，当前使用用户新截取的管理服务窗口。
- `Assets/screenshots/service-manager-overview-zh.png`：管理服务窗口大图，作为主图源文件。
- `Assets/screenshots/tray-menu-zh.png`：托盘右键主菜单。
- `Assets/screenshots/service-manager-zh.png`：管理服务窗口。
- `Assets/screenshots/service-editor-zh.png`：编辑服务和脚本步骤。
- `Assets/screenshots/log-window-zh.png`：实时日志窗口。
- `Assets/screenshots/ai-help-cli-zh.png`：`ai-help` 命令。
- `Assets/screenshots/status-doctor-cli-zh.png`：`status all` 和 `doctor --json`。

## 可选截图

- 变量子菜单：展示最近使用变量排序和 `新增` 入口。
- 执行步骤子菜单：展示 `启动执行` 和 `不启动执行` 分组。
- 失败通知：展示启动失败时的托盘冒泡提示和日志中的错误摘要。
- GitHub Release 下载页：展示 `ServicePilot.exe` 制品。

## 尺寸建议

- README 主图：宽度 1200 到 1600 px，优先截管理服务窗口或托盘菜单组合。
- Release 图：宽度 1000 到 1400 px，突出日志窗口和 CLI。
- 社交预览：宽度 1280 px，高度 640 px，使用管理服务窗口 + 托盘菜单，不要堆太多文字。

## 视觉要求

- 不展示真实内网地址、客户名、令牌或个人路径。
- 窗口缩放保持 100% 或 125%，不要让文字模糊。
- 右键菜单截图要保留任务栏通知区域数字图标。
- 英文截图必须确认按钮文字没有被截断。
- 中文截图优先用于中文 README；英文截图优先用于 `README-en.md`。
