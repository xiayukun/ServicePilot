# 参与贡献

English: [CONTRIBUTING-en.md](CONTRIBUTING-en.md)

感谢你关注 ServicePilot。

## 开发环境

要求：

- Windows
- .NET SDK 8.0+

构建：

```powershell
dotnet build .\ServicePilot\ServicePilot.csproj -c Release
```

## Pull Request 规范

- 保持进程管理安全：停止操作始终使用进程树杀死。
- 不要在脚本执行中引入命令注入或未验证的用户输入。
- 保持配置键和运行时文件名使用英文。
- 行为变更时更新 `README.md`。
- 用 `dotnet build -c Release` 验证。

## 安全规则

脚本执行通过配置的脚本引擎运行用户提供的命令。程序不应在用户配置的动作之外执行任意命令。
