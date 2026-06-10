# 发布说明模板

English template: [release-notes-template-en.md](release-notes-template-en.md)

将此模板复制到 GitHub 发布页面，并根据发布版本编辑。

## ServicePilot vX.Y.Z

英文发布说明：`docs/release-notes-vX.Y.Z-en.md`

### 亮点

- 变更摘要。

### 下载

- `ServicePilot.exe`

### 要求

- Windows
- .NET 8.0 运行时，或使用自包含可执行文件。

### 安全说明

- ServicePilot 会执行用户配置的脚本。
- 运行服务前请检查脚本内容。

### 检查项

- [ ] `dotnet build .\ServicePilot\ServicePilot.csproj -c Release`
- [ ] 临时配置目录下 `ServicePilot.exe ai-help` 和 `ServicePilot.exe doctor --json` 通过
- [ ] 发布并测试单文件可执行程序
- [ ] 截图已更新
- [ ] GitHub 发布制品已上传
