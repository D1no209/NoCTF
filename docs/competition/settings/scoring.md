# 赛事配置与计分

入口：比赛管理 → 配置的模式规则区。普通资料与模式规则独立保存，单题覆盖在比赛题目详情。先核对 mode，只编辑对应结构化分支，不导入旧自由 JSON。

本页描述前四赛制。LiveSolo 使用独立 [胜局/阶段规则设置](../modes/live-solo/settings.md)，没有普通分值、血奖和积分结算，不向通用编辑器提交空规则分支。

在比赛管理“配置”中保存默认规则；比赛题目可以按模式覆盖部分默认项。题库模板不保存比赛分数、提示或 Runner Provider。

## 通用设置

| 设置 | 操作含义 |
| --- | --- |
| 开始与结束时间 | 使用绝对时间，核对页面时区；暂停不推迟绝对结束时间 |
| 最大成员数 | 报名和加入队伍上限 |
| 自动审核 | 合法报名自动 Approved 或进入 Pending |
| 赛中报名 | 控制 Running 时组织变更/重新报名窗口 |
| 每队并发 Runtime 额度 | 覆盖实际所需环境，AWD 要能容纳全部相关题目 |
| 赛道 | 公开、内部与不同计分/资格策略 |
| 题解必交与截止扩展小时 | 截止为 EndAt 加扩展小时，0 表示 EndAt |
| 单题题解开关、收益扣分比例与期限 | 独立于整场 PDF，默认关闭；开启后每题可覆盖默认比例 |
| 榜单可见性 | 正常、冻结、隐藏及生效时间 |
| CTF 赛后练习 | 正式结束后适用的 Practice Runtime 访问 |

比赛“方向”用于组织题目内容，与队伍的“赛道”不同。不要通过题目标签模拟赛道资格。

单题查阅只折扣该队该题的正向收益，保留原处罚、提示费用和人工调分。首次查阅固定比例，动态分值继续按既有模式结算。详细配置、血榜资格和发布流程见 [题解审核](../judging/writeups.md#单题题解设置与审核)。

## CTF 默认规则

### 曲线和血奖字段

| 字段 | 配置方法 |
| --- | --- |
| Initial Points | 首始分值，和最低分保持合法关系 |
| Minimum Points | 衰减后最低基础分，固定分题可与初始相同 |
| Decay Team Count | 按当前曲线参与衰减的队伍规模，不是总报名数 |
| Decay Mode | Fixed、Linear、Quadratic、Exponential、Logarithmic 或 Custom |
| Custom Expression | 仅 Custom 使用，遵循编辑器提供的变量/校验和预览 |
| Score Settlement Mode | DynamicRecalculation 或 AtSolve |
| Blood Rewards | 按名次有序添加，选择 policy 和对应 value |
| Wrong Submission Penalty | 普通错误提交的规则扣分，与平台故障区分 |

为固定测试题设初始/最低都 100，验证一次正确完成得到预期基础分，再逐项增加动态曲线和血奖。自定义表达式先检查少量/临界/最大有效解题人数，不以仅一次预览替代完整政策验证。

血奖 policy 和 value 的单位必须按页面填写，固定 points 与 percentage 不互换。血榜次序与衰减资格独立，先规划赛道再配置；重复正确事实不会生成第二份普通解题奖励。

配置分值曲线、结算模式、血奖和错误罚分。支持 Fixed、Linear、Quadratic、Exponential、Logarithmic、Custom 等曲线；自定义表达式必须通过当前校验，并用样本解题人数预览结果。

| 结算方式 | 后续队伍解题时 |
| --- | --- |
| DynamicRecalculation | 既有正确队伍的基础分按当前解题数重算 |
| AtSolve | 按各队首次有效完成的位置计算基础分 |

线性曲线例：初始 500、最低 100、衰减队数 5，普通依次解题基础分可为 500、400、300、200、100。AtSolve 下两队解题后，下一队报价为 300。血榜资格、是否推动衰减和是否计分由赛道独立控制。

血奖可选择固定值、初始分百分比、解题时分百分比或当前分百分比。AtSolve 下“当前分”和“解题时分”相关奖励采用该完成位置的基础价。修改结算方式会按新规则重算历史，不冻结旧配置结果。

## AWD 默认规则

| 字段 | 单位与意义 |
| --- | --- |
| Hardening Duration | 秒，加固阶段预算 |
| Round Duration | 秒，有效运行时间中的轮次时长 |
| Attack Reward Mode | FixedPerAttack 或 SplitVictimDefensePool |
| Attack Points | 固定攻击奖励 |
| Victim Defense Pool Points | 受害方防守池按规则分配的预算 |
| Checker Interval | 秒，持续健康检查周期 |
| Service Healthy Points | 健康结果奖励 |
| Service Unhealthy Penalty | 不健康结果罚分 |

选择固定奖励与防守池方式时检查当前有效分支，不能把两个数字同时当成相加奖励。攻击资格、有效 Flag 窗口与 Checker 各自验证；轮次更新后读取本次展示，而不是用浏览器墙钟猜轮次。

设置加固时长、轮次时长、攻击奖励方式、攻击分/受害方防守池、Checker 间隔、健康分和不健康罚分。轮次和加固使用有效运行时间，暂停后顺延，不在暂停中继续积累竞技结果。

题目需有轮换注入与服务 Checker。修改间隔和奖励前确认当前轮次行为并通知选手，不仅改一个数字后直接发布。

## AWDP 默认规则

| 字段 | 操作含义 |
| --- | --- |
| Round Duration | 有效轮次时长，秒 |
| Break / Fix Score Curve | 两条独立曲线，不共用一份得分输入 |
| Flag Wrong Penalty | Break Flag 错误时的政策 |
| Exploit Succeeded Penalty | 修复后 exploit 仍成功的失败政策 |
| Service Abnormal Penalty | 正常业务异常的失败政策 |
| Require Break Before Fix | 是否先完成正确 Break 才允许 Fix |
| Max Break / Fix Submissions | 两类尝试预算，按页面允许范围配置 |
| Evaluation Dispatch Mode | Automatic 自动或 Manual 等裁判派发 |

两种 Fix 失败不能合并成“Flag 错误”。资源/存储故障按稳定失败代码处理，不自行消耗另一个动作的次数。手动队列的派发说明见 [提交管理](../judging/submissions.md)。

分别配置 Break/Fix 分值曲线、错误 Flag 罚分、exploit 仍成功罚分、服务异常罚分、是否先 Break 后 Fix、两种尝试上限及自动/手动评测调度。

题目修复包约定、单服务靶机、Checker 和执行预算需要共同满足 Start Gate。人工派发只改变评测时机，不把 Fix 变成另一种 Flag。

## KoH 默认规则

| 字段 | 规则 |
| --- | --- |
| Poll Interval Seconds | 正数，真实轮询节奏 |
| Control Points Per Interval | 非负，每次唯一有效控制观察给分 |

题目 nullable 覆盖可分别继承；显式 0 控制分代表不加分，不是继承。检查端点返回精确 UTF-8 Flag，不使用 JSON，也不添加换行；失败观测不延续上次控制者的得分。

设置正数轮询间隔和非负的每次控制分，比赛题目可分别覆盖。每题有一个共享 Hill 和独立 Control URL。轮询正文必须精确匹配队伍 Flag；缺失/失败观察不补分。

## 单题覆盖与保存

在比赛题目规则中明确选择“继承”或覆盖。例如 CTF 结算方式、曲线和血奖可独立覆盖，不必为了覆盖一项复制全部规则。Nullable 继承和显式 0 是不同含义，不要把 0 当作未设置。

保存后重新打开页面确认持久化值，并检查对应事件与排行榜。Running 状态下修改有效计分规则会刷新历史投影，不会自动重新运行 Checker。正式比赛调整应先说明影响，必要时先暂停并记录原因。

更多结算说明见 [CTF 结算契约](https://github.com/D1no209/NoCTF/blob/HEAD/specs/ctf-score-settlement.md)。
