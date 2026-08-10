# ADR 0001: GameplayFact 与 Dirty 全量排行榜

- 状态：Accepted
- 日期：2026-08-10

## 决策

1. 将 Submission 与 ScoringEvent 合并为 `GameplayFact`，只保存当前唯一有效结果和最新失败原因。
2. 删除判定历史、ProcessingVersion、claim、callback body hash 和判定时 revision；不支持任意历史时点重建。
3. PatchUpload、Hint、AWD Round 通过 `(ReferenceKind, ReferenceId)` 多态关联。PatchUpload 消费由部分唯一索引和事务验证推导。
4. 玩家、管理员、AWD 服务变化和 KoH 轮询都使用同一事实模型；ManualAdjustment 是管理员触发的 Completed/Applied 客观行为。
5. 排行榜放弃 revision/incremental/subscriber-gated projection。影响数据的事务设置 `LeaderboardDirty=true`，Worker 每 15 秒领取脏比赛，从 PostgreSQL 全量投影后替换命名 FusionCache `leaderboards` 的稳定 key。
6. Dirty 期间继续返回旧完整快照；无缓存返回 202。投影失败保留旧快照并重新置 Dirty，无旧快照时返回 503。

## 后果

模型、API、Wolverine 消息、Runner JWT、OpenAPI 和数据库基线均不向旧版本兼容。实现更少、当前状态查询更直接，但无法审计旧判定或按过去配置重建排行榜；需要审计的管理动作仍写 append-only CompetitionEvent。
