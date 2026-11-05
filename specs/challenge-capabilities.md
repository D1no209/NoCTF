# 题库能力与比赛编排

Challenge 保存题面、权限、附件、可选 Runtime 定义和一至五项能力。题库不保存比赛模式。CompetitionChallenge 引用同一个模板，并保存该比赛的展示、发布时间、时间窗口和强类型规则。

## 能力契约

| 能力 | 输入 | 客观结果 |
| --- | --- | --- |
| Break | Flag、服务端确定的候选范围 | 未匹配、匹配或歧义匹配，以及归属证据 |
| Fix | 修补包、目标 Runtime 和指定服务 | 执行完成状态、目标失败或平台故障 |
| Check | 目标 Runtime、指定服务和本次证据要求 | 连接、功能、漏洞和 Flag 读写证据 |
| Control | 已授权目标入口 | 控制者、明确无人控制或无法确定 |
| Questionnaire | 已分配内容版本、小题和选项集合 | 每道小题的当前判定 |

能力定义采用 TPH，同一 Challenge 的 Kind 唯一。命令、环境变量、服务、探针、选项等集合使用关系型有序子表。执行入口分别为 EvaluateBreak、ExecuteFix、ExecuteCheck、ObserveControl 和 EvaluateQuestionnaire，输入和结果均为强类型。

能力执行不处理分数、首解、轮次胜负或参赛资格。服务端从已授权作用范围构建输入，客户端只提交内容和必要资源标识。

## 调用矩阵

| 调用方 | 编排 | 结果用途 |
| --- | --- | --- |
| CTF | Break | 解题、首解、动态计分和进度 |
| AWD | Break、周期 Check，可启用 Fix | 攻击、健康与持久环境维护 |
| AWDP | Break、独立目标中的 Fix→Check | 独立 Break、Fix 成就 |
| KoH | Control | 控制区间与收益 |
| LiveSolo | 每题 Break 或 Fix→Check | 按正式接收顺序裁定对局 |
| 独立考试、练习 | Questionnaire | 阅卷、成绩、重试及纠正 |

部署、保存规则、发布和开始前检查所需能力、目标服务、Runtime、Flag 签发方式和检查要求。失败返回具体配置及稳定错误码。

## Runtime 与修改保护

模板保存 Container 或 OVA 构造定义、命名服务、资源需求、命令、环境变量、内部端口及访问入口。比赛规则决定共享或每队分配、Flag 生命周期、TTL、续期和目标用途。Provider 与 RunnerPool 由平台选择。

Runtime 用途使用 Player、Practice、TemplateTest、AttackTarget、FixTarget、ScopedFixTarget；后两者分别表示比赛中的独立验证目标和执行作用范围中的独立验证目标。每次重置创建新的 Runtime UUID。旧执行和回调必须核验目标 UUID 及当前身份。活动 Runtime 或在途执行依赖的技术配置受到修改保护；未被当前执行使用的能力可以独立配置。删除能力前检查比赛、事实和考试引用。Questionnaire 更新创建不可变版本。

## 当前事实与证据

正式提交或计划观察分别使用 BreakAttempt、FixAttempt、CheckObservation、ControlObservation 或 QuestionnaireAttempt。每个 GameplayFact 保存处理状态、当前比赛判定和稳定失败码；能力结果按 `(GameplayFactId, CapabilityKind)` 唯一保存，探针、Flag 证据和小题结果使用关系子表。

Fix→Check 使用同一个 FixAttempt，分别保存 Fix 和 Check 当前结果，只投影一次 Fix 成就。阶段为 Queued、Applying、Checking、Finalizing。回调保存当前证据，Runner 确认工作及检查资源收尾完成；验证 Runtime 停止且容量释放后，事实进入最终状态并触发成绩与对局裁定。

AWDP 和 LiveSolo 使用独立验证目标，新修补尝试使用干净环境。AWD 修补作用于本队当前持久 Runtime，不单独得分；失败后的部分修改保留，重复消息不会自动重新修补。未知外部修改先确认原执行终止，再解除冲突操作限制。

## 消息与权限

业务事务提交后通过 IPostCommitMessagePublisher 发布；Pending、Queued 和中断状态可由恢复扫描重新分发。回调验证事实、Runtime、执行作用范围、签名和有效期。重复结果保持幂等，冲突和迟到结果具有明确处理结果。

选手只读取 AvailableActions 和授权投影。标准答案、私有 Flag、Checker 命令、认证信息及内部地址保持在授权边界内。独立问卷事实不会进入比赛排行榜、进度图或比赛通知。

API 见 [接口契约](api.md)，问卷流程见 [Questionnaire](questionnaire.md)，运行资源见 [Runtime](runtime.md)。

## 题库授权测试

执行作用范围由 CapabilityExecutionScope 区分 GameplayFact 与 TemplateTest。能力执行器返回客观结果，正式调用方显式转换成当前事实证据；模板测试结果不能转换成正式事实。

Break、Control、Questionnaire 同步返回。Fix、Check、FixAndCheck 异步受理，状态由 TemplateTest Runtime 的关系型当前测试记录拥有。Interactive 与 CapabilityVerification 分别用于交互调试和独立能力验证；环境查询、续期和停止只操作交互环境。

每次 Fix 或 FixAndCheck 测试建立干净独立环境，结束时回收。Fix 单项不执行 Check，允许仅提供 Fix 的题目测试。Check 读取指定交互测试环境和服务端要求。重复请求、消息及回调核验当前身份；停止、重置或到期后的旧回调无法更新结果。资源清理确认以前保留容量占用。

前端只测试已保存配置，未保存编辑会禁用启动操作。测试不创建 GameplayFact、成绩、首解、进度、正式 Session 或比赛通知。授权结果只包含客观证据，不暴露认证令牌、内部地址或 Checker 凭据。
