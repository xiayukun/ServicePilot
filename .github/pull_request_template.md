## 摘要

English template: [pull_request_template-en.md](pull_request_template-en.md)

## 变更


## 验证

- [ ] `dotnet build .\ServicePilot\ServicePilot.csproj -c Release`
- [ ] 系统托盘行为已检查
- [ ] 日志窗口行为已检查
- [ ] CLI / AI 命令行为已检查，或说明不适用

## 安全说明

- [ ] 这个变更不会引入命令注入或不安全的进程处理
- [ ] 脚本执行已正确隔离和验证
- [ ] 配置兼容性已保留，或迁移方式已记录
