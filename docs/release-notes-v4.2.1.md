ServicePilot 4.2.1 改进持续输出日志的查看体验：用户选择展开或摘要后，后续新产生的合并组会继续保持相同模式；搜索长日志时也会自动横向定位到命中内容。

## 改进

- **新日志组继承展开/摘要模式**：点击“展开”后，之后产生的合并组默认保持展开；点击“摘要”后，新组继续折叠。单独展开某组或搜索自动展开仍只影响对应组，不会修改全局模式。
- **长日志搜索自动横向定位**：上一个/下一个搜索命中位于长行右侧时，日志窗口会同时滚动纵轴和横轴，让目标内容直接进入可视区域，无需再手动拖动底部滚动条。

## 验证

- 用户已完成实际使用校验并确认通过。
- 真实 WPF 回归测试覆盖“展开后追加新组”“切回摘要后追加新组”和 600 字符长行末尾搜索，2/2 通过。
- 本地 Debug 构建为 0 警告、0 错误；Release 发布目录仅包含一个自包含 `ServicePilot.exe`，隔离 `doctor --json` 为 0 错误、0 警告。
- GitHub Actions 的 Restore、Build、单文件 Publish、CLI 冒烟测试和制品上传全部通过。

## 要求

- Windows
- 使用发布页自包含 `ServicePilot.exe` 时不需要单独安装 .NET 运行时。

---

🌐 English: [Changelog](https://github.com/xiayukun/ServicePilot/blob/main/CHANGELOG-en.md)
