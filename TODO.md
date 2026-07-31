# NoCTF 待办入口（旧清单已归档）

本文件原有清单基于已经废弃的单 API、进程内 Channel、插件式 GameMode、Penetration
模式和禁止 Wolverine/Worker 等旧架构，不能再作为实现或验收依据。

后续工作只从以下权威来源确定：

1. [`AGENTS.md`](AGENTS.md)：仓库硬约束；
2. [`docs/README.md`](docs/README.md)：产品与技术规范入口；
3. [`NoCTF-backend-handoff-2026-07-24.md`](NoCTF-backend-handoff-2026-07-24.md)：
   已完成纵切、验证证据和当前交接顺序。

不要根据本文件历史、旧提交或已勾选项目恢复兼容层、旧协议或已否决的部署方案。真实
Kubernetes、Libvirt 和生产运维工作必须等待目标环境参数与部署细则；收到这些信息前，
不得猜测拓扑、凭据、网络或存储配置。
