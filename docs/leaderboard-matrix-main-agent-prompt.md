# NoCTF 统一排行榜矩阵模型重构

请在 `E:\SourceCode\NoCTF` 中重新设计并实现统一排行榜数据模型。目标是让 CTF、AWD、AWDP、KoH 共用同一套排行榜传输结构，由后端提供权威计分投影，前端根据比赛模式、题目方向和 Slot 内容决定具体渲染方式。

## 一、开始前检查

开始修改前必须完整阅读并严格遵循：

1. 根目录 `AGENTS.md`
2. `backend/src/NoCTF.API/ClientApp/AGENTS.md`
3. 最新 `HANDOFF.md`
4. `TODO.md`
5. 当前 `git status`
6. 当前 `git diff`
7. 最近的 `git log`
8. 当前 Leaderboard、GameplayFact、CompetitionEvent、CompetitionChallenge、Team、FusionCache、SignalR 实现
9. CTF、AWD、AWDP、KoH 的权威计分与轮次结算文档

不得覆盖用户或其他 Agent 的未提交修改。

本功能属于排行榜投影和协议重构，不应新增业务数据表。不得新增 `leaderboard_slots`、`scoreboard_columns`、`scoreboard_rounds`、`score_entries` 或其他排行榜持久化表。

优先复用：

- `competitions`
- `competition_challenges`
- `gameplay_facts`
- `competition_events`
- `teams`
- FusionCache `leaderboards`
- Wolverine 投影刷新
- 现有比赛题目接口
- 现有 CompetitionHub/SignalR

如确实发现现有模型无法满足某项不可变约束，必须先说明具体原因并征得确认，不能擅自增加表或迁移。

## 二、设计目标

排行榜采用统一的规范化二维矩阵：

```text
Matrix[teamIndex][columnIndex] = ScoreSlot
columnIndex -> competitionChallengeId + roundId
```

矩阵语义：

- 纵轴是队伍；
- 横轴是“比赛题目 × 轮次”；
- 每一支队伍对应一行；
- 每一道题在每一轮对应一个横轴位置；
- 每个 Slot 表示某队在某题某轮的操作与权威计分结果；
- 当前轮允许展示实时操作，但轮次型模式不得提前展示尚未结算的最终分数；
- Challenge 信息从排行榜中独立出去，通过现有比赛题目接口获取；
- 题目信息、轮次信息和提交者信息不得在每个 Slot 中重复传输；
- 前端负责布局、图标、颜色、折叠和图表；
- 后端负责所有权威计分、衰减、除法、取整、血榜、处罚和轮次结算。

统一协议不代表抹平模式语义。后端必须提供强类型的操作类型、结果和计分组成，前端不得依靠字段是否为空猜测模式。

## 三、接口分层

将排行榜数据拆为三类资源。

### 1. 比赛题目目录

复用现有比赛题目批量接口，不在排行榜快照中重复嵌入完整 Challenge。

```ts
type CompetitionChallengeCatalog = {
  competitionId: string
  revision: number
  items: CompetitionChallengeInfo[]
}

type CompetitionChallengeInfo = {
  id: string
  title: string
  direction: string
  category: string
  order: number
  published: boolean
}
```

要求：

- `id` 必须是 `CompetitionChallengeId`，不能使用全局题库模板 `ChallengeId` 代替；
- 题目标题、方向、分类、顺序只从该接口获取；
- 前端按 `CompetitionChallengeId` 建立映射；
- 不允许跨接口使用数组下标关联 Challenge；
- 题目接口必须批量返回当前调用者有权查看的全部题目，不能按题目逐个请求；
- 有历史计分但题目被取消发布时，工作人员历史视图必须仍能获得最小题目显示信息；
- 不得泄露未发布题面、Flag、Checker、Runtime Definition 或其他受保护字段。

`revision` 可以是现有题目修订信息计算出的目录版本或稳定响应版本，不要求新增数据库列。

### 2. 排行榜矩阵结构

提供低频变化的排行榜结构：

```ts
type ScoreboardSchema = {
  competitionId: string
  mode: "Ctf" | "Awd" | "Awdp" | "Koh"
  revision: number
  challengeCatalogRevision: number
  rounds: ScoreboardRound[]
  columns: ScoreboardColumn[]
}

type ScoreboardRound = {
  id: string
  number: number
  startAt: string
  endAt: string
  settledAt: string | null
  state: "Pending" | "Running" | "Settling" | "Settled"
}

type ScoreboardColumn = {
  index: number
  competitionChallengeId: string
  roundId: string | null
}
```

要求：

- `(competitionChallengeId, roundId)` 在同一 Schema 中唯一；
- `column.index` 在同一个 Schema revision 内稳定；
- `column.index` 从零开始连续排列；
- 无轮次模式可以使用 `roundId = null`；
- 不得为了轮次创建新业务表；
- 如果现有比赛模式以轮次编号而非 UUID 表示轮次，应使用稳定、确定性的轮次标识；
- 轮次的开始、结束和结算时间只保存一次，不在每道题和每个 Slot 中复制；
- Schema revision 表示完整 Schema 响应内容的稳定版本：新题目、题目移除、新轮次、横轴结构变更，以及轮次开始、结束、结算时间或状态变化时都必须更新。
- 相同 Schema 内容重复投影时 revision 必须稳定；客户端仅可在 revision 未变化时复用已缓存 Schema。

### 3. 动态排行榜快照

频繁更新的数据独立返回：

```ts
type ScoreboardSnapshot = {
  competitionId: string
  version: number
  schemaRevision: number
  generatedAt: string
  currentRoundId: string | null
  actors: ScoreboardActor[]
  teams: ScoreboardTeamRow[]
}

type ScoreboardActor = {
  index: number
  userId: string
  displayName: string
}
```

`version` 是排行榜投影版本，不要求新增数据库表。它可以由投影生成过程产生并随 FusionCache 快照保存，但必须满足：

- 新权威投影替换旧投影时版本发生变化；
- 同一快照重复读取版本稳定；
- 前端可以判断更新是否比当前快照更新；
- 旧版本响应不得覆盖新版本页面状态。

提交者目录要求：

- Entry 使用 `actorIndex` 引用提交者；
- 同一提交者只在 `actors` 中出现一次；
- 不得返回邮箱、Token、Flag、IP、登录信息或其他敏感资料；
- 用户匿名化后使用现有匿名化显示名称；
- 系统自动行为不伪造提交者，使用 `actorIndex = null`。

## 四、队伍行模型

```ts
type ScoreboardTeamRow = {
  teamId: string
  teamName: string
  rank: number
  rankingState: "Eligible" | "Banned" | "Disqualified"
  totalScore: number
  /** 当前轮次窗口之外、已计入 totalScore 的权威净分。 */
  scoreOutsideWindow: number
  globalAdjustments: ScoreboardAdjustment[]
  slots: ScoreSlot[]
}

type ScoreboardAdjustment = {
  id: string
  kind: "ManualAdjustment" | "CompetitionPenalty" | "BanRecalculation"
  occurredAt: string
  actorIndex: number | null
  earnedPoints: number
  deductedPoints: number
  netPoints: number
}
```

要求：

- 每支队伍在快照中最多出现一次；
- `rank` 和 `totalScore` 由后端权威投影生成；
- `scoreOutsideWindow` 由后端权威投影生成；非轮次模式固定为 0，轮次窗口覆盖整场时也为 0；
- 历史轮次窗口中的 `scoreOutsideWindow` 同时包含窗口之前和之后、已计入当前总分的 Slot 净分，不允许前端自行累计；
- 前端不得自行重新排名；
- 被封禁或取消资格的队伍必须使用明确状态表达；
- 不属于具体题目和轮次的人工调分、比赛级处罚放入 `globalAdjustments`；
- 不允许为全局调分伪造 Challenge 或虚拟轮次；
- `slots` 使用稀疏数组，没有操作、状态和分数的单元格不传。

## 五、ScoreSlot 完整模型

```ts
type ScoreSlot = {
  /** 对应 ScoreboardSchema.columns[columnIndex]。 */
  columnIndex: number

  /**
   * Pending：当前没有可展示的权威分数。
   * Provisional：后端允许公开的当前实时分数，之后仍可能变化。
   * Settled：已完成权威结算。
   */
  scoreState: "Pending" | "Provisional" | "Settled"

  /** Pending 时均为 null。 */
  earnedPoints: number | null
  deductedPoints: number | null
  netPoints: number | null

  breakdown: ScoreBreakdown[]
  entries: ScoreEntry[]
}

type ScoreBreakdown = {
  kind:
    | "Solve"
    | "Attack"
    | "Defense"
    | "Availability"
    | "Control"
    | "Penalty"
    | "BloodAward"
    | "Hint"
    | "ManualAdjustment"

  successfulCount: number
  attemptCount: number
  earnedPoints: number
  deductedPoints: number
  netPoints: number
}
```

要求：

- 同一 TeamRow 内 `columnIndex` 唯一；
- `columnIndex` 必须存在于当前 Schema；
- Slot 不重复保存题目标题、方向、轮次编号、轮次时间、队伍名称；
- `Pending` 时三个分数字段必须为 `null`，不能用零冒充尚未结算；
- `Provisional` 和 `Settled` 时三个分数字段必须有值；
- 后端必须提供 `netPoints`，前端不能执行自定义公式；
- 前端可以验证简单算术一致性，但验证失败时不得覆盖后端值；
- AWDP 当前轮未结束时只能展示操作和统计，不得提前生成最终轮次分；
- AWDP 历史已结算轮次不受后续轮次分值变化影响；
- 封禁、解禁或明确要求的重盘操作由后端重新生成权威快照；
- `attemptCount >= successfulCount >= 0`；
- AWDP 必须分别提供 `Attack` 与 `Defense`；
- AWD 必须保留攻击、防御、可用性等现有权威维度；
- KoH 使用 `Control`；
- CTF 使用 `Solve`，并可以附加 `BloodAward`、`Hint`；
- 前端根据 `kind` 决定列、图标、颜色和图表；
- 不允许创建大量可空的模式专属平铺字段。

## 六、ScoreEntry 完整模型

```ts
type ScoreEntry = {
  /** 稳定 ID，用于实时合并和幂等去重。 */
  id: string

  kind:
    | "Solve"
    | "Attack"
    | "Defense"
    | "Availability"
    | "Control"
    | "Penalty"
    | "BloodAward"
    | "Hint"
    | "ManualAdjustment"

  outcome: "Pending" | "Succeeded" | "Failed" | "Rejected"
  actorIndex: number | null
  targetTeamId: string | null
  occurredAt: string
  settledAt: string | null
  earnedPoints: number | null
  deductedPoints: number | null
  netPoints: number | null
  award: "FirstBlood" | "SecondBlood" | "ThirdBlood" | null

  /** awardPoints 是 earnedPoints 的组成部分。 */
  awardPoints: number
}
```

要求：

- `id` 必须稳定且不可因重新读取投影而改变；
- 使用现有 GameplayFact、CompetitionEvent 或权威投影标识；
- 不得生成随机的响应级 ID；
- Entry 不得包含明文 Flag；
- Entry 不得包含 Patch 内容、Checker 输出、Runtime 凭据或内部失败过程；
- 参赛者响应只能包含现有公开权限允许的信息；
- 工作人员敏感详情继续走现有受保护接口；
- 一个 Slot 可以包含多个 Entry；
- AWDP 同题同轮可以同时存在多次 Attack、Defense 和处罚；
- 系统行为的 `actorIndex` 为 `null`；
- `awardPoints` 必须由后端按实际血榜表达式结算；
- 前端不得获得表达式后自行计算；
- 未结算 Entry 的三个分数字段为 `null`；
- 已结算但没有得分或扣分时使用零。

Entry 完整历史可能很大，因此主快照只返回排行榜需要展示的计分记录或受控摘要。如完整操作历史会导致快照无界增长，应提供 Slot 详情查询并使用现有强类型签名游标分页。主快照必须保留 `breakdown`、已影响分数的记录、必要的最近记录、完整记录数量。不得静默截断而不返回总量或下一页游标。

Slot 详情分页必须绑定主快照的 `dataAsOf`。读取明细时，应以该时点之前最后一条不可变 `GameplayFactAdjudicated` 事件还原裁决状态和结果；后续重判不得改写已经返回的历史榜单详情。对于缺少历史裁决事件的旧数据，只允许在事实自身尚未晚于 `dataAsOf` 时使用当前状态，否则按当时仍待处理展示。若旧事件未保留区分不同计分身份所必需的完整信息，详情不得猜测单条得分或扣分；此时返回 `null`，以主快照的 Slot 聚合分值为权威结果。

## 七、模式结算语义

### CTF

- 无轮次 CTF 使用 `roundId = null`；
- 当前动态题目分值可以使用 `Provisional`；
- 解题、提示扣分和血榜奖励由后端投影；
- 前端不得根据解题人数重新计算动态题目分值；
- 比赛结束后的最终状态为 `Settled`。

### AWDP

- 每道题每轮有独立 Slot；
- 当前轮允许显示 Attack、Defense 的操作次数和成功次数；
- 当前轮最终分数在结算前必须为 `Pending`；
- 轮次结束时，后端按该轮成功攻击队伍数、防御队伍数和对应独立衰减曲线进行结算；
- Attack 与 Defense 使用独立 `ScoreBreakdown`；
- 轮次结束后 Slot 变为 `Settled`；
- 后续轮次衰减变化不得修改之前轮次的已结算分数；
- 禁赛和解禁可以触发明确的全量重盘；
- Fix 一次性验证结果与 Break 结果都必须映射为对应 Entry；
- Fix 不得建模为 Flag submission；
- 排行榜只能展示实际已结算轮次得分，不能根据当前成功数预测本轮最终得分。

### AWD

- 使用题目轮次横轴；
- Attack、Defense、Availability 按现有权威模式映射；
- 当前轮操作可以实时展示；
- 是否展示临时分数必须服从现有 AWD 权威结算规则；
- 轮次结算后使用 `Settled`。

### KoH

- 使用 `Control` Entry 和 Breakdown；
- 控制权切换、持有时间或轮次得分由后端计算；
- 前端不得根据时间差自行累计权威分数；
- 当前控制状态与最终结算分数必须区分。

## 八、算术不变量

后端投影和测试必须验证：

```text
ScoreSlot.netPoints
= ScoreSlot.earnedPoints - ScoreSlot.deductedPoints

ScoreBreakdown.netPoints
= ScoreBreakdown.earnedPoints - ScoreBreakdown.deductedPoints

ScoreSlot.earnedPoints
= 所有计入该 Slot 的 Breakdown.earnedPoints 合计

ScoreSlot.deductedPoints
= 所有计入该 Slot 的 Breakdown.deductedPoints 合计

Team.totalScore
= 所有可计分 Slot.netPoints
+ ScoreboardTeam.scoreOutsideWindow
+ 所有 globalAdjustments.netPoints
```

对于 `Pending` Slot：

```text
earnedPoints = null
deductedPoints = null
netPoints = null
```

对于 `Provisional` 或 `Settled` Slot，三个分数字段均不得为 null。

分数的除法、衰减、对数、指数、自定义表达式、百分比和取整全部在后端执行。协议中的分数必须是最终整数结果，不向前端暴露要求前端执行的公式。

## 九、后端实现要求

1. 复用现有排行榜投影能力，不创建排行榜业务表。
2. 由 Infrastructure 从 PostgreSQL 的 GameplayFact、CompetitionEvent、CompetitionChallenge 和 Team 生成矩阵投影。
3. 排行榜继续使用 `Competition.LeaderboardDirty`。
4. 维护 Agent 继续扫描脏比赛并生成完整权威投影。
5. 投影完成后原子替换 FusionCache 中的稳定排行榜快照。
6. 不按订阅者数量决定是否投影。
7. 队伍按 rank 和稳定 TeamId 排序。
8. 横轴按题目 order、轮次 number 和稳定 ID 排序。
9. Entry 按 occurredAt 和稳定 ID 排序。
10. Worker 重投不得产生重复 Slot、重复 Entry 或重复分数。
11. GameplayFact 相同事实重复处理必须幂等。
12. 被封禁或解禁后执行全量重盘，不能只修改前端本地状态。
13. 不得将 ScoreSlot 建模为 Domain Entity。
14. 不得将 ScoreboardColumn 或 ScoreboardRound 建模为新业务表。
15. Schema、Snapshot 和 Entry 都是查询投影或协议模型。
16. 查询不得为每支队伍、每个 Slot 或每个 Entry 发起独立 SQL。
17. 使用集合式查询和有界投影。
18. 禁止产生 Team × Challenge × Round 的全量空对象矩阵。
19. 只生成非空 Slot。
20. 长比赛历史必须支持按轮次窗口或游标读取旧 Slot 详情。
21. 不得加载整场全部 GameplayFact 后在内存逐项筛选。
22. 所有 bounded concept 使用 enum 或值对象，禁止字符串穿透 Domain/Application。

## 十、API 要求

所有新增或修改 Endpoint 必须：

- 使用强类型 FastEndpoints 基类；
- 使用 `ExecuteAsync`；
- 使用 `Results<T1, ...>`；
- 使用具体 HttpResults；
- 使用 `TypedResults`；
- Endpoint 只处理路由、认证、授权、验证和协议映射；
- 业务投影放在 Application/Infrastructure；
- 不手工序列化；
- 不调用 `SendAsync`、`WriteAsJsonAsync`；
- 不手写状态码；
- 不创建共享 `Dtos.cs` 或 `Models.cs`；
- 每个 Endpoint 的请求、响应和 Validator 放在其拥有文件中；
- 分页使用现有签名游标；
- 游标签名绑定 endpoint、competitionId、userId、筛选条件和轮次窗口。

概念接口包括：比赛题目目录、排行榜 Schema、排行榜 Snapshot、单个 Team/Column Slot 详情。实际路由命名遵循现有项目风格。

如果现有接口可以在不破坏职责的前提下演进，应优先修改现有接口，避免保留两套长期并行的排行榜协议。项目处于 Alpha 阶段，不要求保留旧排行榜响应兼容层。删除废弃协议、旧 DTO、旧 mapper 和旧前端解析代码。

接口变化完成后必须使用项目工具导出 OpenAPI，确认两份 OpenAPI 制品一致，重新生成 TypeScript SDK，再次执行导出和生成并验证第二次生成无差异。检查生成 SDK 未被手工修改。

## 十一、实时更新设计

SignalR 不直接广播完整题目目录、完整 Schema 或完整排行榜快照，只广播版本通知：

```ts
type ScoreboardUpdated = {
  competitionId: string
  version: number
  schemaRevision: number
  challengeCatalogRevision: number
}
```

前端行为：

1. 收到比当前更新的 `version` 后刷新 Snapshot；
2. `schemaRevision` 变化时刷新 Schema；
3. `challengeCatalogRevision` 变化时刷新题目目录；
4. 多个短时间连续事件合并为一次刷新；
5. 正在刷新时又收到事件，完成后必须再执行一次 trailing refresh；
6. 旧请求晚返回不能覆盖新快照；
7. 断线重连后获取最新完整 Snapshot；
8. 页面后台不可见时可以延迟渲染，但不能丢失最终刷新；
9. 不允许每次 GameplayFact 创建就把完整排行榜 JSON 直接推送给所有客户端。

行级增量不是本阶段强制要求。如果实现 `ScoreboardPatch`，必须带 `baseVersion`、`version`、`schemaRevision`、变化队伍和移除队伍。只有 `baseVersion` 与客户端版本一致时才能应用；版本不连续、乱序、重连或 Schema 变化时必须回退到完整 Snapshot。

## 十二、前端实现要求

前端只能调用生成 SDK。禁止手写 REST URL、请求 DTO、业务枚举和失败码；禁止使用 `$fetch` 绕过 SDK；禁止在前端执行计分公式；禁止根据可空字段猜测 GameMode；禁止在 SignalR 中接收完整未类型化排行榜对象。

加载流程：

1. 并行获取 Challenge Catalog、Scoreboard Schema 和 Scoreboard Snapshot；
2. Catalog 按 `CompetitionChallengeId` 建立 Map；
3. Round 按 `roundId` 建立 Map；
4. Column 按 `column.index` 建立 Map；
5. Actor 按 `actor.index` 建立 Map；
6. TeamRow 的 Slot 使用 `columnIndex` 定位横轴；
7. 缺失 Challenge 时显示可理解占位状态并触发一次目录刷新；
8. 缺失 Schema revision 时拒绝渲染旧 Slot并重新获取 Schema；
9. 请求失败保留上一份成功快照；
10. 加载失败不能伪装为空排行榜；
11. 刷新期间保留上一份内容并显示轻量 loading；
12. 同一刷新只发一次请求；
13. 页面卸载时取消或失效在途请求。

渲染职责：

- CTF 显示题目解题、血榜、提示和动态分；
- AWDP 分开显示 Attack 和 Defense；
- AWD 显示 Attack、Defense、Availability；
- KoH 显示 Control；
- 题目方向决定图标和视觉样式；
- 轮次可以折叠、横向滚动或按时间窗口切换；
- 点击 Slot 打开详情；
- 前端可以计算布局、宽度、颜色和简单展示合计；
- 前端不得重新计算权威得分、排名和轮次结算。

AWDP 当前轮只显示实时攻击/防御成功数和提交数，不显示预测得分；已结算轮次分别显示攻击得分、防御得分和扣分；队伍总分只包含已结算轮次；详情页按轮次查看攻击者、操作时间和结算结果；不显示 Flag、Patch 内容和 Checker 内部过程。

## 十三、数据量控制

必须避免每个 Slot 重复题目标题、题目方向、轮次时间和队伍名称；避免每个 Entry 重复提交者名称；避免每个 SignalR 消息重复完整快照；避免为每支队伍生成完整空矩阵。

使用 Challenge Catalog、Scoreboard Schema、Actor Catalog、稀疏 Slot、按需 Slot 详情、签名游标、版本失效通知、请求合并和 FusionCache 快照。

为以下规模补性能测试：

```text
100 支队伍
20 道题
50 个轮次
100,000 个理论单元格
```

要求：

- 不生成 100,000 个空 Slot；
- SQL 查询次数不随 Team × Challenge × Round 线性增长；
- 主快照只包含非空 Slot；
- 序列化结果不重复嵌入 Challenge；
- 读取单个轮次窗口不加载整场全部历史；
- 投影查询不存在 N+1。

## 十四、后端测试

### 通用矩阵

- Challenge 不嵌入 Snapshot；
- Column 正确关联 CompetitionChallengeId 和 RoundId；
- 同一 `(Challenge, Round)` 不生成重复 Column；
- Column index 稳定且连续；
- 同队同列只生成一个 Slot；
- 无事实位置不生成空 Slot；
- Actor 在目录中去重；
- Entry actorIndex 正确；
- Slot Breakdown 合计正确；
- Slot earned、deducted、net 一致；
- Team total 与所有 Slot 和 Adjustment 一致；
- 全局调分不伪造 Challenge；
- banned/disqualified 状态正确；
- 重复投影输出确定性一致；
- Wolverine 重投不产生重复 Entry。

### CTF

- 无轮次题目使用 `roundId = null`；
- 动态题目分值为后端 Provisional；
- 正确解题生成 Solve Entry；
- 一二三血生成正确 award 和 awardPoints；
- Hint 扣分进入对应 Slot；
- 前端协议没有公式字段；
- 比赛结束后 Slot 转为 Settled。

### AWDP

- 每道题每轮产生独立 Column；
- 当前轮 Attack/Defense 操作可见；
- 当前轮 Slot 分数字段保持 null；
- 当前轮结束后一次性结算；
- Attack 与 Defense 使用独立 Breakdown；
- 多次攻击和 Fix 的 attemptCount、successfulCount 正确；
- 下一轮分值变化不修改上一轮；
- 重复轮次结算不重复加分；
- Worker 重投不重复结算；
- 封禁和解禁触发重盘；
- Fix 不被建模为 Flag action；
- Patch 和 Checker 敏感内容不进入 ScoreEntry。

### AWD

- 每轮 Attack、Defense、Availability 映射正确；
- 当前轮和已结算轮区分正确；
- Checker 重投不重复生成分数；
- 暂停期间不错误结算；
- 恢复后继续正确轮次。

### KoH

- 控制权切换映射为 Control Entry；
- 当前控制状态与结算分数区分；
- 控制持续时间由后端计算；
- 并发夺取只有权威结果进入投影；
- 结束后不继续累计。

### PostgreSQL 与缓存

- 使用真实 PostgreSQL，禁止 EF InMemory 代替关系行为证明；
- 使用真实 Redis/FusionCache；
- dirty competition 被维护 Agent 发现；
- 投影原子替换缓存；
- 失败投影不覆盖上一份成功快照；
- 多次 dirty 合并正确；
- 大数据查询次数有明确上限；
- 同时投影同一比赛保持幂等；
- 不同比赛可独立投影。

## 十五、API 与协议测试

- Schema、Snapshot、Slot Detail 返回 typed 200；
- 不存在比赛返回 typed 404；
- 无权限返回 typed 403 或现有资源隐藏语义；
- Participant 不能读取工作人员专属记录；
- Observer 保持只读；
- Judge、Manager、Owner、Administrator 权限符合现有规则；
- 已删除比赛历史访问符合现有归档规则；
- 游标签名篡改、跨比赛、跨用户、跨 Team/Column 均拒绝；
- OpenAPI 使用字符串枚举；
- OpenAPI 两份制品一致；
- SDK 生成两次幂等；
- 不存在手写 URL、DTO 或协议 enum。

## 十六、前端测试

- Catalog、Schema、Snapshot 并行加载；
- ChallengeId 正确关联题目；
- 不使用跨接口 challengeIndex；
- Column 正确关联 Round；
- 稀疏 Slot 正确放入矩阵；
- 缺失 Challenge 时显示占位并刷新；
- Schema revision 变化后重新加载；
- Snapshot version 较旧时忽略响应；
- 连续实时事件合并刷新；
- 刷新期间再次收到事件会执行 trailing refresh；
- 断线重连刷新完整 Snapshot；
- 请求失败保留上一份排行榜；
- 失败不显示空态；
- 当前 AWDP 轮次不显示预测得分；
- 已结算 AWDP 轮次显示攻击分和防御分；
- CTF、AWD、AWDP、KoH 使用同一协议并采用不同渲染；
- 前端不存在计分公式；
- Slot 详情加载失败不清空主排行榜；
- 中英文文案完整；
- 不出现原始失败码或错误翻译键；
- typecheck 和 production build 通过。

## 十七、E2E 验收

使用可丢弃测试比赛分别验证：

### CTF

- 两支队伍和至少两道题；
- 解题后排行榜更新；
- 血榜奖励正确；
- Challenge 不随每次 Snapshot 重复返回；
- 实时通知只触发版本刷新。

### AWDP

- 两支以上队伍、两道题和至少三个完整轮次；
- 当前轮显示 Attack/Defense 操作但不显示预测分；
- 轮次结束后填入权威分数；
- 下一轮分值变化不修改上一轮；
- 排行榜总分按轮累计；
- 封禁和解禁后正确重盘；
- 攻击和防御详情可分别查看。

### AWD

- 多轮 Checker；
- Attack、Defense、Availability 正确；
- 暂停和恢复不错误结算。

### KoH

- 三支队伍依次取得控制；
- Control Entry 和轮次分正确；
- 并发控制权只产生一个权威结果。

浏览器必须检查：Network 中题目目录不会随每次排行榜更新重复请求，除非目录 revision 变化；SignalR 不携带完整排行榜 JSON；多次提交被合并刷新；Console 无新增错误；页面刷新后与后端权威事实一致。

## 十八、完成门禁

完成后运行：

1. 后端 Release build；
2. 后端全部非 Integration TUnit；
3. 真实 PostgreSQL Integration；
4. 真实 Redis/FusionCache Integration；
5. Wolverine 重投和并发 Integration；
6. CTF E2E；
7. AWD E2E；
8. AWDP E2E；
9. KoH E2E；
10. EF pending model changes 检查；
11. OpenAPI 导出；
12. TypeScript SDK 生成；
13. OpenAPI 与 SDK 二次生成幂等检查；
14. 前端全部测试；
15. TypeScript typecheck；
16. lint，仓库存在脚本时执行；
17. 前端 production build；
18. `git diff --check`；
19. 检查生成 SDK 未被手工修改；
20. 检查未新增业务表和迁移。

不得将 skip、blocked 或未执行报告为通过。Kubernetes 或 Libvirt 环境未配置时，必须准确报告阻塞，不得伪造通过。

## 十九、提交与 HANDOFF

按可审阅范围创建独立提交，建议顺序：

1. `refactor(leaderboard): introduce normalized matrix projection`
2. `feat(api): expose leaderboard schema and snapshot`
3. `refactor(frontend): render unified leaderboard matrix`
4. `test(leaderboard): cover mode projections and realtime refresh`
5. `chore(release): bump alpha version`
6. `docs(handoff): record leaderboard matrix redesign`

要求：

- 不把 HANDOFF 混入功能提交；
- 不把版本提交混入功能提交；
- 不覆盖协作者修改；
- 合并冲突时逐文件理解双方语义；
- 不使用破坏性 git 操作；
- 未经主线程当前明确授权，不推送或部署。

HANDOFF 必须记录旧排行榜模型的问题、新矩阵模型、Challenge 独立接口、Schema/Snapshot/Slot/Entry 协议、四种模式映射、AWDP 轮次结算语义、实时版本通知、OpenAPI/SDK 变化、数据库和迁移状态、测试命令和精确结果、未覆盖环境及原因、提交哈希、推送和部署状态。

## 二十、最终验收标准

只有同时满足以下条件才算完成：

- 四种比赛模式共用同一排行榜矩阵协议；
- Challenge 不嵌入频繁刷新的 Snapshot；
- 题目通过 `CompetitionChallengeId` 关联；
- 轮次信息只传一次；
- 横轴明确映射 Challenge + Round；
- 每支队伍是一行；
- Slot 使用稀疏数组；
- Slot 包含得分、扣分、净分、提交时间和提交者引用；
- AWDP Attack 与 Defense 独立；
- 当前 AWDP 轮次不提前显示预测分；
- 已结算轮次分数不会被后续衰减变化修改；
- 前端不执行权威计分公式；
- SignalR 不推送完整排行榜；
- 旧响应不会覆盖新响应；
- 失败不伪装为空排行榜；
- 不新增排行榜业务表；
- OpenAPI 和生成 SDK 同步；
- 完整测试通过；
- HANDOFF 已更新；
- 工作树状态和提交范围清晰。
