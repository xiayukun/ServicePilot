# 隐私说明

English: [PRIVACY-en.md](PRIVACY-en.md)

ServicePilot 是一个本地 Windows 工具。

## 读取的数据

ServicePilot 读取：

- 用户配置的工作目录
- 配置编辑器中输入的脚本内容
- 运行中服务的进程输出（stdout/stderr）

## 写入的数据

ServicePilot 写入：

- `%APPDATA%/ServicePilot/config.json`
- `%TEMP%/ServicePilot/` 中的临时脚本文件

## 网络访问

ServicePilot 本身不需要网络访问。用户配置的服务可能根据需要访问网络（例如 `npm run dev` 启动开发服务器）。

它不会把文件、路径、日志、配置或机器名上传到远程服务。

## 敏感路径

配置可能包含本地路径和脚本内容。公开分享前，请将 `config.json` 和日志输出视为潜在敏感信息。
