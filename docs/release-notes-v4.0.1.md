ServicePilot 4.0.1 是一次修复版本：解决日志折叠在真实运行中错位的问题。

## 修复

- **日志折叠错位（多线程乱序）**：进程的 `stdout` 与 `stderr` 由两个并发线程读取，此前可能导致后产生的日志行先进入日志页面。喂给依赖顺序的合并/折叠状态机后，会出现「折叠组头平铺在上、明细堆在底部、错误起始行错位」。现在同一步骤的输出按真实读取顺序串行提交，折叠与日志顺序恢复正确。

  说明：经 `merge-script test` 验证，合并函数本身逻辑正确，无需改动；此问题为程序端的多线程顺序缺陷。

## 要求

- Windows
- 使用发布页自包含 `ServicePilot.exe` 时不需要单独安装 .NET 运行时。

## 安全说明

- ServicePilot 会执行用户配置的本地脚本；合并脚本同样是用户提供的 C# 代码，运行在应用进程内，请只使用你信任的合并脚本。
- AI 帮助内容会包含本机 exe 绝对路径，只建议发送给可信个人 AI 助手。

---

🌐 English: [Changelog](https://github.com/xiayukun/ServicePilot/blob/main/CHANGELOG-en.md)
