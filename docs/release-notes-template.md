# 发布说明模板

将此模板复制到 GitHub 发布页面，并根据发布版本编辑。发布说明只维护中文正文，英文用户在正文底部通过 `CHANGELOG-en.md` 链接查看，不再单独维护每版英文发布说明。

## ServicePilot vX.Y.Z

### 亮点

- 变更摘要。

### 要求

- Windows
- .NET 8.0 运行时，或使用自包含可执行文件。

### 安全说明

- ServicePilot 会执行用户配置的脚本。
- 运行服务前请检查脚本内容。

---

🌐 English: [Changelog](https://github.com/xiayukun/ServicePilot/blob/main/CHANGELOG-en.md)

## GitHub Release 正文规范

- 只写中文正文，英文用户统一通过底部链接跳转 `CHANGELOG-en.md`，不再单独维护每版英文发布说明文件。
- 正文不要以 `# ServicePilot X.Y.Z` 重复标题开头（发布页已显示标题）。
- 不要写「下载 / Download」小节：下载统一走发布页下方 **Assets** 里的 `ServicePilot.exe`，不要在正文放任何指向 `latest`/版本包的手写链接。
- 正文底部固定一行英文入口，仅链接 Changelog：

  ```text
  ---

  🌐 English: [Changelog](https://github.com/xiayukun/ServicePilot/blob/main/CHANGELOG-en.md)
  ```

### 发布检查项

- [ ] `dotnet build .\ServicePilot\ServicePilot.csproj -c Release`
- [ ] 临时配置目录下 `ServicePilot.exe ai-help` 和 `ServicePilot.exe doctor --json` 通过
- [ ] 发布并测试单文件可执行程序
- [ ] 截图已更新
- [ ] GitHub 发布制品（`ServicePilot.exe`）已上传到 Assets
