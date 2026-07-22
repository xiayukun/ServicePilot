ServicePilot 2.0.0 是一次配置模型重构版本。

## 亮点

- 引入 `Action` / `Composite` 模型：动作负责执行命令，组合动作负责编排动作。
- 活跃配置迁移到 `config.v2.json`，旧 `config.json` 保留不删除。
- 旧服务级预设变量迁移为动作级变量。
- 服务和模板编辑器支持组合动作成员选择和排序。
- 中文界面和文档统一使用“动作”术语，动作类型显示为“动作 / 组合动作”。
- 日志窗口移除独立启动按钮，改为从“运行动作”统一运行；日志页签按动作懒创建，并在动作进入运行时切换。
- 日志窗口会合并非错误的 webpack 进度输出，减少高频构建日志导致的卡顿。
- CLI `start SERVICE` 运行第一个组合动作，`step run` 可运行动作或组合动作。
- 模板导入导出会保留组合动作成员关系。

## 验证

- `rtk dotnet build ServicePilot.sln`
- 隔离空配置 `doctor --json`
- v1 `config.json` 自动迁移为 `config.v2.json`
- CLI `service add --step ...` 自动创建 `启动` 组合动作
