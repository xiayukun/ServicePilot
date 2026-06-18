# 进程运行器调研

[English](process-runner-research-en.md)

这份文档记录 ServicePilot 强化过程中参考过的进程管理器和命令运行器。后续如果继续吸收或明确拒绝某些行为，需要同步更新这里。

## 已查看项目

- [PM2](https://pm2.io/docs/runtime/reference/pm2-cli/)：带守护进程的进程管理器，提供 `start`、`stop`、`restart`、`list`、`logs` 和适合自动化的命令。
- [Supervisor](https://www.supervisord.org/subprocess.html)：有明确的 `STARTING`、`RUNNING`、`STOPPING`、`EXITED`、`FATAL` 等状态，适合参考状态机。
- [Foreman](https://ddollar.github.io/foreman/)：Procfile 运行器，一个进程类型对应一条命令，并有明确的停止超时。
- [Honcho](https://honcho.readthedocs.io/en/latest/using_procfiles.html)：Procfile 运行器，会命名进程实例并注入有用的环境变量。
- [Overmind](https://github.com/DarthSim/overmind)：Procfile 管理器，支持单个进程重启/连接，适合本地开发工作流。
- [concurrently](https://github.com/open-cli-tools/concurrently)：跨平台多命令运行器，支持输出前缀和某个命令退出后结束其他命令的策略。
- [npm-run-all](https://github.com/mysticatea/npm-run-all)：支持顺序和并行编排 npm scripts，兼顾 Windows 使用。
- [mprocs](https://github.com/pvolok/mprocs)：TUI 进程运行器，支持独立输出、单进程启动/停止/重启、配置文件和远程控制。
- [nodemon](https://github.com/remy/nodemon)：文件变化后重启应用，会区分崩溃、等待和重启状态。
- [watchexec](https://github.com/watchexec/watchexec)：跨平台文件变化命令运行器，支持进程组、重启模式、过滤和防抖。
- [entr](https://github.com/eradman/entr)：小型文件变化命令运行器，支持持久进程重启。
- [Task](https://taskfile.dev/)：跨平台 YAML 任务运行器，用显式任务定义替代零散 shell 串联。
- [just](https://github.com/casey/just)：强调可读 recipe、shell 选择和可发现命令调用的命令运行器。

## 对 ServicePilot 的结论

- 生命周期状态必须明确。ServicePilot 应继续区分 `Starting`、`Running`、`Stopping`、`Stopped`、`Completed`、`Error` 和 `StartFailed`。
- 服务执行到最后一个动作后就应该离开 `Starting`。最后一步成功退出可变为 `Completed`；失败退出应变为 `StartFailed`。
- 长时运行动作自行退出，对本地开发服务来说通常是启动或运行失败，应明确显示为 `StartFailed`。
- 停止应按进程树处理，只有确实停止失败时才告警。无窗口 console 命令不能依赖 `CloseMainWindow`。
- 日志需要前缀、稳定顺序、有限内存和直接操作入口。日志窗口应提供当前服务的启动、停止、重启。
- CLI 应覆盖托盘可见操作，并在适合自动化的场景返回机器可读输出。
- 模板和配置文件应减少重复配置，但命令内容必须保持显式、可审查。
- 未来增加 watch/autorestart 时，应引入防抖、重启策略，以及“预期退出”和“异常退出”的区别。
