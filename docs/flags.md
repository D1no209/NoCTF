# Flag 规范

## 存储与比较

所有正确答案只存 `challenge_flags`，选手提交原文只存 `gameplay_facts.value`。Flag 明文保存，不加密、不 HMAC 替代、不做不可逆隐藏。日志允许记录 Flag；玩家 API 不得泄露其他队/正确答案。

精确匹配固定为：UTF-8、ordinal、区分大小写、固定时间比较；不 Trim、不 Unicode 归一化。合法 Flag 或匹配表达式为 1..4096 UTF-8 bytes且不含 NUL。

题目 Owner/Manager 可以将无 Runtime 的 CTF 静态答案配置为正则表达式。正则匹配整段提交内容、区分大小写，并使用 .NET NonBacktracking 引擎和 100ms 超时；不支持回溯型结构（例如反向引用和环视）。CTF Container/Compose Runtime 必须使用 `FlagSource=PerTeam`：平台为每支队伍生成一条 Flag，并在创建 Runtime 时注入题目配置的环境变量。CTF Runtime、AWD、AWDP、KoH 生成的动态 Flag 始终只做精确比较，内部 `MatchKind=Exact` 只是判题实现，不是管理员维护的静态匹配规则。切换题目模式或启用 Runtime 前必须先删除或改回其所有正则 Flag。

CTF 赛后练习判题复用完全相同的 Flag scope、Team、有效时间、SHA-256 候选缩小与 ordinal 原文比较，
并要求本队对应题目的 `Practice` Runtime 正在运行且未到期。它只返回 `Correct`/`Wrong`，不写入
GameplayFact、CompetitionEvent、Notification，不触发血榜、分数或排行榜刷新。

先用普通 SHA-256 索引缩小候选，再比较原文。SHA-256 不是安全边界。

## 时间窗

唯一有效性规则为半开区间：

```text
(ValidStart is null || ValidStart <= GameplayFact.OccurredAt)
&& (ValidUntil is null || GameplayFact.OccurredAt < ValidUntil)
```

不使用 Active/Status。CTF/模板静态 Flag 通常无界；AWD 每轮严格 RoundStart..RoundEnd，上一轮在边界立即失效。比赛 Paused 时提交被拒绝，Resume 会顺延当前逻辑轮次与 ValidUntil。

## Scope

ChallengeFlag 必须二选一：

- `ChallengeId`：模板级静态答案；所有引用模板的 CompetitionChallenge 实时使用。
- `CompetitionChallengeId`：比赛实例、Team、Runtime、RandomOne、AWD Round、KoH Control Flag。

模板 Flag 不允许 TeamId。判题同时加载实例 Flag与关联模板 Flag，再按模式/选择策略筛选。

## SpecificationKind / SpecificationId

首版 Kind：

- `Attachment`：SpecificationId=ChallengeAttachment.Id；
- `AwdRound`：SpecificationId=Round Number 编码 Guid；
- `RuntimeDefinition`：绑定 versioned Competition/Challenge configuration JSON 中某个稳定 `definitionId`；definitionId 一经被 Flag 引用不得复用或原位改变语义；
- `RuntimeGeneration`：SpecificationId=RuntimeInstance.Id；用于 AWDP 攻击 Runtime，每个 Team/CompetitionChallenge/Generation 一条；
- `Hint`：仅 GameplayFact HintUnlock 的多态 Reference 使用，不用于 ChallengeFlag。

Kind/Id 同时为空或同时非空。

AWD Round 是 1-based 整数 1..99,999,999，不建 Round 表。编码为 Guid 文本首段八位十进制：

```text
1   -> 00000001-0000-0000-0000-000000000000
132 -> 00000132-0000-0000-0000-000000000000
```

必须提供强类型 `AwdRoundSpecificationId.FromRound/Parse`，按文本构造，不能依赖 Guid(byte[]) 字节序。

## PerTeam Flag 模板

配置继承：Platform default -> Competition override -> CompetitionChallenge override；越具体优先。结构：

```text
header: string
bodyTemplate: string
leetLiteralText: bool
```

header 非空结果为 `header{body}`；Header 不 Leet。只 Leet bodyTemplate 的字面文本，占位符展开值不 Leet。模板变更只影响以后生成。未指定或仅含空白的 header 自动使用 `flag`；未指定或仅含空白的 bodyTemplate 自动使用 `[GUID]`，即默认生成 `flag{<随机 UUIDv4>}`。

题库 `Challenge.DefinitionJson` 只配置 Runtime/Checker 以及环境变量或目标文件等注入位置；不同比赛可使用不同 Flag 头，因此题目级模板覆盖只写入 `CompetitionChallenge.RulesJson`，由比赛题目管理界面维护。

支持：

```text
[GUID] / [GUID:D] / [GUID:N]
[TEAMID] / [TEAMID:D] / [TEAMID:N]
[CHALLENGEID] / [CHALLENGEID:D] / [CHALLENGEID:N]
[COMPETITIONCHALLENGEID] / [COMPETITIONCHALLENGEID:D] / [COMPETITIONCHALLENGEID:N]
[COMPETITIONID] / [COMPETITIONID:D] / [COMPETITIONID:N]
[TEAMHASH]
[TEAMHASH:n]          n=8..64
[RANDOM:n]            n=8..128
```

GUID 是每条 Flag 新生成的 UUIDv4，同一模板内多次出现复用同一个值。CHALLENGEID 是全局模板 Id，COMPETITIONCHALLENGEID 是比赛题目实例 Id。Guid 默认小写 D 格式、允许连字符；N 是小写无连字符。每个 RANDOM occurrence 独立使用 CSPRNG 和固定 Base62 字母表 `0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz` 无偏生成。未知占位符、未知格式或 n 越界使保存配置失败。

### TEAMHASH

每个 Competition 创建时生成不可修改的 32-byte CSPRNG FlagDerivationSecret：

```text
HMAC-SHA-256(
 key = Competition.FlagDerivationSecret,
 message = UTF8(
   "noctf:teamhash:v1:"
   + CompetitionId:D + ":"
   + CompetitionChallengeId:D + ":"
   + TeamId:D))
```

输出小写 hex；默认前 32 字符，n 可取 8..64。Secret 不通过 API/DTO/日志返回，且与 JWT、InvitationToken 独立。

## SafeLeetV1

算法写死，不可配置替换表；只配置 leetLiteralText。逐字符一次扫描：50% 保留，50% 用 CSPRNG 从表中均匀选择替换；结果不递归。表键按 ASCII 大小写不敏感匹配，候选按表原样输出。若整段没有改变，随机选择一个存在“不同于原字符”候选的位置，并从不同候选中强制替换一次；没有可替换字符则原样返回。只生成 ASCII 字母/数字，不产生 `$` 等 Shell 元字符；字母可替字母，数字可替数字。

```text
a -> A,4          n -> N,m,M
b -> B,8          o -> O,0
c -> C,k,K        p -> P
d -> D            q -> Q,g,G,9
e -> E,3          r -> R
f -> F            s -> S,5
g -> G,q,Q,6,9    t -> T,7
h -> H            u -> U,v,V
i -> I,l,L,1      v -> V,u,U
j -> J            w -> W
k -> K,c,C        x -> X
l -> L,i,I,1      y -> Y
m -> M,n,N        z -> Z,2

0 -> 8,9,o,O
1 -> 7,i,I,l,L
2 -> 7,z,Z
3 -> 8,e,E
4 -> 9,a,A
5 -> 6,s,S
6 -> 5,8,9,g,G
7 -> 1,2,t,T
8 -> 0,3,6,9,b,B
9 -> 0,4,6,8,g,G,q,Q
```

最终 Flag 再验证字节长度。“冲突”只指同一 CompetitionChallenge、同一 evaluator purpose/重叠窗口内，候选原文已经映射到另一个 Team；不做平台全局唯一。一次生成最多产生 10 个候选：前 9 个冲突就重新生成，第 10 个仍冲突则直接保存这个重复值，不进行第 11 次。模式在反查到不同团队时必须 PlatformFailed/AmbiguousFlagMatch，不能猜测。

## CTF 策略

`FlagSelectionPolicy.All` 默认：全部附件可见，所有符合当前 scope/window 的 Flag 可正确。

`RandomOnePerTeam` 只用于多附件题：

保存/Start Gate 要求至少 2 个未删除 Attachment，且每个候选至少绑定一条未删除模板 Flag；否则为 AttachmentSelectionInvalid。未绑定 Attachment 的通用模板 Flag 可以同时存在，但不替代每个候选自身的绑定要求。

1. 玩家调用单一 attachment 下载 API，不能传 AttachmentId；
2. 首次下载在 Team/题事务锁内从不同 AttachmentId CSPRNG 均匀抽一个；
3. 把该 Attachment 对应的所有模板 Flag 复制为 CompetitionChallengeId+TeamId+Attachment Specification；模板中未绑定 Attachment 的通用 Flag 不复制但始终可用于所有队；其他 Attachment 绑定的模板 Flag 不参与该队判定；
4. 返回选中附件；以后始终相同；
5. 未抽取前不泄露候选文件名；新增附件只影响未抽取队；已选附件不可删除。

不建 assignment 表，团队级 Flag 的 SpecificationId 即持久化选择。

步骤 3 创建的是独立、固定的团队答案事实。之后修改/新增模板 Flag 只影响尚未抽取的团队；不得原位同步或替换已抽取团队的副本。管理者若确需改变某已抽取团队的答案，必须直接修改对应 CompetitionChallenge+Team Flag，并按需手动重判现有 GameplayFact。这里的生成事实不属于 Challenge 题面“实时引用、不做快照”的范围。

All 判定使用模板中未绑定 Attachment 的 Flag和全部 Attachment-bound Flag；RandomOne 判定只使用未绑定的通用模板 Flag、已选 Attachment 的团队副本，以及该实例上与 Attachment 无关的合法 Flag。不得直接加载所有模板 Attachment Flag 后跳过选择过滤。

## Runtime / KoH / AWD

- CTF Container/Compose PerTeam：每队每题/RuntimeDefinition 固定一条，SpecificationKind=RuntimeDefinition；Start 前在 `(CompetitionChallengeId,TeamId,DefinitionId)` 锁内查询，不存在则生成；所有 Generation 复用。
- OVA：只支持 Static Flag，平台不注入。
- KoH：Running 前为全部有效队/题生成固定 Control Flag，SpecificationKind=RuntimeDefinition；新批准队/新发布题增量生成；Team 详情仅返回本队值。
- AWD：每个 RoundStart 后为每队/题生成一条，Specification=AwdRound，窗口严格为该逻辑轮次；失败注入重试同一条，不重新生成。
- AWDP v2：每次创建 `AwdpAttack` Runtime generation 时幂等生成一条
  Specification=RuntimeGeneration 的精确 Flag。Challenge 定义只配置环境变量名或绝对文件路径，
  Competition/CompetitionChallenge 配置 Flag 模板。Provider Running 且注入成功后才设置 ValidStart；
  Stop、Reset、过期或失败设置 ValidUntil。AWDP 不使用 AwdRound，不允许出题人手工建立正常攻击路径的
  静态 Break Flag，也不把动态 Flag 返回给玩家 API。

## 手动预生成

Owner/Manager/Judge 可调用 generate-missing，并按 CompetitionChallengeId/TeamId 筛选；只生成缺失的 CTF PerTeam 与 KoH Control Flag，不替换已有值，不抽取 RandomOne，不预生成 AWD Round。调用在 Competition advisory lock 下同步执行，逐目标返回稳定失败原因但不返回 Flag 值/Id/数量；单个目标失败不回滚其他目标。Observer 只读。
