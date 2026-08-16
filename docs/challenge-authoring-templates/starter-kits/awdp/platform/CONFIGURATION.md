# AWDP 平台配置抄录表

本文件用于把已经在 NoCTF 管理界面中确认的配置随题目一同交付，不是可导入的内部 JSON。不要手写 `schemaVersion`、比赛 ID、队伍 ID、规格 ID、Runner Pool 或 Runtime Provider。

## 题库模板

- 游戏模式：`AWDP`
- Break Flag：精确匹配，由题库 Flag 管理，不放入公开附件
- 分配语义：`PerTeam`
- Runtime：Container；不使用 Compose 或 OVA
- 目标镜像：`待填写`
- 公网端口 / URL：不配置
- 唯一内部端口：`8080`
- Patch 入口：`fix.sh`
- Patch 命令：`["/bin/sh", "{entrypoint}"]`
- Patch 超时：`60` 秒
- 就绪等待：`30` 秒
- 上传上限：`256 MiB` 或题目审定值
- Checker 镜像：`待填写`
- Checker 目标端口：`8080`
- Checker 超时：`待填写`

## 比赛题目规则

- Break / Fix 结算：`Milestone` 或 `PerRound`
- Break / Fix 分值：`待填写`
- 规则违规、服务不可用、错误 Break、失败 Fix 罚分：`待填写`
- 先 Break 后 Fix：`待填写`
- 最大 Break / Fix 次数：`待填写`；后端值 `<=0` 表示无限
- 评测派发：`Automatic` 或 `ManualBatch`

完整字段和边界以包内 `NOCTF-DELIVERY.md` 及平台当前管理界面为准。
