ServicePilot 4.2.2 是依赖与构建流程维护版本，保持现有服务配置和操作方式。

## 改进

- 日志合并脚本编译组件更新至 Microsoft.CodeAnalysis.CSharp.Scripting 5.9 系列，仍支持 .NET 8。
- GitHub 构建更新为 checkout v7、setup-dotnet v6、upload-artifact v7，继续提供 Windows x64 自包含单文件程序。
- 无需迁移用户服务或模板配置，不涉及数据库变更。

## 验证

- 通过项目构建、单文件发布、隔离 CLI 配置诊断和 5 项进程并发回归。
- 验证单文件程序中的脚本编译、日志进度摘要、API/前端启动通知及热更新通知去重。
- 既有无效脚本仍会报告编译错误，升级不会自动修改用户脚本。

---

🌐 English: [Changelog](https://github.com/xiayukun/ServicePilot/blob/main/CHANGELOG-en.md)
