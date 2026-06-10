# 同类项目代码调研

[English](competitive-research-en.md)

这份文档记录 ServicePilot 上线前参考过的同类项目。调研来源包括公开 GitHub 仓库、官方文档和本地克隆源码。调研目标不是照搬功能，而是明确 ServicePilot 应该吸收什么、避免什么、如何形成自己的定位。

## 结论摘要

ServicePilot 最值得坚持的方向：

- 面向 Windows 本地开发，而不是生产守护进程。
- 托盘 GUI 和 CLI 双入口，CLI 要足够适合 AI 调用。
- 服务配置长期保存，步骤、变量、模板都要可审查。
- 日志要有限、可复制、可搜索，不能因为长日志拖死 UI。
- 停止进程必须按进程组处理，尤其是 npm/Vite 子进程。
- 不做复杂 DAG。首版保持“服务 + 有序步骤 + 变量 + 模板”。

## 项目对比

| 项目 | 类型 | 值得学习 | ServicePilot 的取舍 |
| --- | --- | --- | --- |
| [PM2](https://github.com/Unitech/pm2) | Node.js 进程管理器 | 守护进程、JSON 状态、日志和环境覆盖 | 不做生产集群和负载均衡；保留 JSON 状态和环境变量思路 |
| [concurrently](https://github.com/open-cli-tools/concurrently) | 多命令运行器 | 输出前缀、成功条件、kill-others 策略 | 不做一次性命令拼接工具；吸收清晰输出和失败策略 |
| [npm-run-all](https://github.com/mysticatea/npm-run-all) | npm scripts 编排 | 顺序/并行、Windows 进程树结束 | ServicePilot 更偏持久服务配置；继续强化 Windows 停止可靠性 |
| [Foreman](https://github.com/ddollar/foreman) | Procfile 运行器 | `.env`、Procfile、进程 formation、停止超时 | 不引入 Procfile 作为主配置；未来可做导入 |
| [Overmind](https://github.com/DarthSim/overmind) | Procfile + tmux 管理器 | 长驻控制进程、单进程重启/连接、socket 命令 | tmux 不适合 Windows GUI；保留“运行实例可被 CLI 控制” |
| [Hivemind](https://github.com/DarthSim/hivemind) | 简化 Procfile 管理器 | 环境变量配置、端口递增、进程组信号 | 不自动分配端口；重视进程组停止 |
| [Goreman](https://github.com/mattn/goreman) | Go Procfile 管理器 | RPC 控制、Windows CTRL_BREAK、部分行日志缓冲 | 保留命令管道控制；未来可增加更温和的停止阶段 |
| [Task](https://github.com/go-task/task) | YAML 任务运行器 | 任务变量、包含、依赖、跨平台 | 不做完整任务 DSL；变量保持字符串简单可见 |
| [just](https://github.com/casey/just) | 命令运行器 | 可发现 recipe、补全、面向人类的命令入口 | ServicePilot 继续强化 `ai-help` 和命令发现 |
| [WinSW](https://github.com/winsw/winsw) | Windows 服务包装器 | Windows 服务模型、日志滚动、生命周期 hook | ServicePilot 不安装系统服务；未来可借鉴日志轮转 |
| [Servy](https://github.com/aelassas/servy) | Windows 服务管理 GUI/CLI | GUI + CLI、日志、健康检查、恢复策略 | ServicePilot 用托盘和 AI CLI 做差异化，不急着做生产恢复 |
| [Listr2](https://github.com/listr2/listr2) | 任务列表状态机 | 细粒度任务状态、渲染器、并发和失败控制 | 当前步骤状态已够用；未来可加 retry/rollback |

## 代码级观察

### PM2

PM2 的源码围绕进程守护、环境合并、日志路径、JSON 配置和 `jlist/describe` 等机器可读状态展开。它的优势是生产运维能力强，缺点是明显偏 Node.js 生态和长期守护。

ServicePilot 已吸收：

- `status --json`、`list --json`、`service get --json`。
- 变量注入和环境覆盖的思路。

暂不吸收：

- 集群、负载均衡、生产守护。
- 复杂 ecosystem 配置。

### concurrently

concurrently 的代码把命令、输出流、前缀、成功条件、取消信号拆得比较清楚。它是一次性命令运行器，不负责长期保存服务。

ServicePilot 已吸收：

- stdout/stderr 不等于成功/失败，最终状态必须由退出码和运行时异常决定。
- 日志输出要可读、可复制、可限制。

后续可参考：

- 更显式的“任一命令失败时是否停止其他命令”策略。

### npm-run-all

npm-run-all 对 Windows 进程树结束有专门处理，也区分顺序和并行 npm scripts。它适合项目内 package scripts。

ServicePilot 已吸收：

- Windows 不能只杀父进程，必须处理子进程树。
- 有序步骤必须一个成功后再跑下一个。

### Foreman / Overmind / Hivemind / Goreman

这些 Procfile 系项目共同强调：一个配置文件声明多个进程、运行时可以控制单个进程、日志需要前缀、停止需要信号和超时。

ServicePilot 已吸收：

- 运行中的托盘实例由命令管道接收 CLI 控制。
- 单服务、单步骤可独立启动/停止/查看日志。
- 停止路径优先取消和 Job Object，失败时兜底杀进程树。

暂不吸收：

- Procfile 作为主配置。
- tmux attach/connect 模型。
- 自动端口分配。

### Task / just

Task 和 just 的优点是把“怎么运行项目”写进仓库，命令可发现、可复用、可交给同事或 CI。

ServicePilot 的区别：

- 它更适合跨多个本地文件夹集中管理。
- 它能从托盘和日志窗口直接操作。
- 它把 AI 入口做成 `ai-help` 和 JSON 查询，而不是让 AI 阅读项目 DSL。

后续可参考：

- 从 Taskfile、justfile、package.json 导入服务。
- 生成一个给 AI 的项目启动摘要。

### WinSW / Servy

WinSW 和 Servy 更靠近 Windows 服务管理，适合生产或半生产场景，强调恢复、日志轮转、健康检查、服务依赖。

ServicePilot 的定位不同：

- 不安装系统服务。
- 不要求管理员权限。
- 更适合每天频繁启动、切换变量、查看日志、让 AI 帮忙操作。

后续可参考：

- 日志轮转或导出。
- 健康检查。
- 自动重启策略。

### Listr2

Listr2 的价值在于任务状态清晰，适合复杂任务编排 UI。ServicePilot 当前已经有 `NotRun`、`Running`、`Succeeded`、`Failed`、`Skipped`、`Cancelled`。

后续可参考：

- retry 状态。
- rollback 状态。
- 更丰富的步骤结果摘要。

## 对 README 和功能的落地改动

本轮已经落地或明确要求保留：

- README 把“AI 可操作本地开发服务”作为主要卖点。
- 新增 `ServicePilot.exe ai-help`。
- CLI 补齐服务步骤变量维护：`step variables`、`step variable-add`、`step variable-remove`、`step variable-clear`。
- CLI 补齐模板步骤变量维护：`template step-variables`、`template step-variable-add`、`template step-variable-remove`、`template step-variable-clear`。
- CLI 配置保存保持 async 贯通，避免命令模式阻塞和残留进程。
- 新增 `doctor [--json]` 配置体检，吸收健康检查/可诊断性的方向，但保持为本地配置扫描。
- CHANGELOG 采用初始版本口径，不暴露上线前内部打磨流水账。

## 后续路线

- 配置导入/导出。
- 从 `package.json`、Taskfile、justfile、Procfile 扫描候选服务。
- 可选自动重启策略。
- 日志导出和轻量轮转。
- 健康检查。
- 给 AI 的更严格 JSON schema 文档。
