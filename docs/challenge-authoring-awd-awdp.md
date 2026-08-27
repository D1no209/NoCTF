# NoCTF AWD、AWDP 出题规范

本文面向题目 Owner、题目 Manager、比赛 Owner/Manager/Judge，以及负责制作 Runtime、Checker 和 Fix 验证环境的出题人员。文中描述的是 NoCTF 当前实现所接受的配置和运行语义；如果旧题目包、历史文档或其他平台的做法与本文冲突，以本文及 [AWD 模式规范](game-modes/awd.md)、[AWDP 模式规范](game-modes/awdp.md) 为准。

新题目可以直接复制 [AWD 出题模板](challenge-authoring-templates/awd-challenge-template.md) 或 [AWDP 出题模板](challenge-authoring-templates/awdp-challenge-template.md)，逐项填写并随题目资产一同评审。需要直接交付给出题人的完整文件树时，使用 [AWD / AWDP Starter Kit](challenge-authoring-templates/starter-kits/README.md)；其中包含可构建服务、Checker、Fix 样例、冒烟测试和 tar.gz 打包脚本。

## 1. 先理解三个配置层级

NoCTF 将“可复用题目模板”和“某场比赛中的题目”严格分开。绝大多数出题错误都来自把字段放错层级。

| 配置位置 | 保存什么 | 不应保存什么 |
| --- | --- | --- |
| 题库模板 `Challenge` | 题面、方向、附件、静态 Flag、Runtime 镜像与启动方式、Checker、动态 Flag 注入位置、AWDP 补丁入口 | 比赛 ID、队伍 ID、题目顺序、题目分值、比赛专属 Flag 前缀、Runner Pool |
| 比赛配置 `Competition` | 轮次长度、硬化期、全局计分、默认 Checker 周期、AWDP 默认 Break/Fix 规则 | 镜像、容器命令、Checker 镜像、补丁脚本 |
| 比赛题目 `CompetitionChallenge` | 自定义题目名称、顺序、发布状态、题目规则覆盖、比赛专属 Flag 模板 | Runtime Provider、Runner Pool、平台内部 UUID |

关键结论：

- Runtime、Checker 和“如何把 Flag 写进容器”是题库模板的可复用执行规格。
- 具体比赛使用什么 Flag 头、Flag 正文模板和计分覆盖，必须在“比赛管理 → 题目 → 题目规则”中配置。不同比赛可以引用同一模板而使用不同 Flag 模板。
- 出题人不需要手写 `schemaVersion`、队伍 ID、规格 ID、生效时间或失效时间；前端会维护这些内部字段。
- Challenge 定义只选择 Container/Compose 等可移植规格。实际使用 Docker 还是 Kubernetes、使用哪个 Runner Pool，由平台配置统一决定。

## 2. 通用镜像和 Runtime 要求

### 2.1 镜像

- 镜像可以使用普通 tag，也可以使用 digest；NoCTF 不强制固定 digest。
- 生产比赛可自行选择普通 tag、不可变 tag 或 digest；NoCTF 不强制固定 digest。若覆盖同一 tag，变化只影响之后创建的新 Runtime UUID。
- 镜像必须能够从平台 Runner 所在环境拉取。私有仓库的认证和网络可达性由平台运维负责。
- 题目服务应监听容器内固定端口，不能在容器中写死宿主机地址或随机宿主端口。
- Docker 公网入口必须配置为容器端口到宿主端口 `0` 的映射，由 Docker 分配随机宿主端口。

推荐基础镜像结构：

```dockerfile
FROM python:3.12-slim-bookworm

COPY --chmod=0444 server.py /app/server.py
USER 10001:10001
EXPOSE 8080
ENTRYPOINT ["python", "-B", "/app/server.py"]
```

不要依赖以下能力：

- `privileged`、宿主 PID/IPC/网络命名空间；
- Docker Socket、宿主路径挂载、设备映射；
- 运行时安装软件或临时访问平台数据库、Redis、对象存储；
- 固定宿主端口、固定容器名称或特定 Runner 主机名。

### 2.2 资源与安全选项

每个 Runtime 必须设置正数的内存、CPU 和 PID 上限。建议从最小可用值开始压测，再保留 20%～30% 峰值余量。

安全选项的兼容规则如下：

- `no-new-privileges`、只读根文件系统和非 root 运行可以按题目兼容性选择。
- 开启非 root 时，Docker 镜像必须声明数字形式且非零的 `USER`。
- 开启只读根文件系统时，题目需要写入的数据必须放在平台允许的可写位置或内存文件系统中；先在真实 Runner 上验证 Flag 注入和修复脚本。
- `capAdd` 没有需求时保持空数组；不要为了“省事”授予额外 capability。

### 2.3 网络、端口和地址

- Container 的对外端口在“对外端口”中声明；内部仅供 Checker 访问的端口放在“内部端口”。
- URL 模板只允许 `http`、`https`、`tcp`、`udp`、`ssh`。常用 HTTP 模板为 `http://{HOST}:{PORT}/`。
- `OwnerOnly` 地址只返回给本队；AWD 攻防入口需要 `Participants`，硬化期结束后平台才会向参赛队伍提供对手入口。
- `Isolated` 表示题目网络与其他 Runtime/平台网络隔离，不应把它理解为绝对的无公网保证；题目本身仍不得依赖外网。
- Checker 与目标在隔离网络内通信，不通过选手看到的公网随机端口。

## 3. AWD 出题规范

### 3.1 AWD 的运行模型

每支获批队伍拥有独立 Runtime。平台按有效运行时间推进轮次，为“比赛 + 题目 + 队伍 + 轮次”生成精确匹配的动态 Flag，并调用注入命令写入该队 Runtime。Checker 周期性检测服务状态，攻击者提交其他队伍当前轮次 Flag 获得攻击收益。

AWD 只允许：

- `PerTeam` 分配；
- Container 或 Compose Runtime；
- `AwdRotation` Flag 来源；
- 平台生成、精确匹配的动态 Flag。

出题人不得手工创建队伍 Flag，也不得把 AWD Fix、服务状态或轮次建模为静态 Flag。

### 3.2 推荐出题流程

1. 在题库中新建 `AWD` 模板，完成题面和附件。
2. 配置 Container 或 Compose Runtime，设定资源、内部/公网端口、URL 和安全选项。
3. 配置 Flag 注入命令。
4. 配置 Checker 镜像、超时和目标服务。
5. 在比赛中添加该模板。
6. 在比赛配置中设置硬化期、轮次、攻击收益、服务分和 Checker 默认周期。
7. 在比赛题目规则中按需覆盖单题计分、Checker 周期和比赛专属 Flag 模板。
8. 使用至少两支测试队伍完成轮换、攻击、宕机恢复、暂停恢复和比赛结束验收。

### 3.3 比赛配置

当前默认值如下：

| 字段 | 默认值 | 含义 |
| --- | ---: | --- |
| 硬化期 | 0 秒 | 开赛后只允许加固、隐藏对手入口且拒绝攻击 |
| 轮次长度 | 300 秒 | 动态 Flag、攻击去重和服务分的逻辑轮次 |
| 攻击奖励模式 | 固定奖励 | 每个合法攻击按固定分奖励 |
| 单次攻击分 | 50 | 固定奖励模式下攻击者所得 |
| 受害方防守池 | 100 | 某题某轮首次被攻破后受害队扣除的总额 |
| Checker 周期 | 30 秒 | 同一题服务检测间隔 |
| 服务正常分 | 100 | 完整轮次最终服务状态为 Up 时所得 |
| 服务异常罚分 | 50 | 完整轮次最终服务状态为 Down 时扣除 |

攻击奖励模式：

- `FixedPerAttack`：每个不同攻击者的首次合法攻击获得“单次攻击分”；受害队在该题该轮只扣一次防守池。
- `SplitVictimDefensePool`：受害队仍只扣一次防守池；该题该轮所有合法攻击者均分防守池，单队获得 `Floor(防守池 / 攻击者数量)`。

硬化期、轮次长度是比赛级字段。单题可覆盖攻击模式、攻击分、防守池、Checker 周期、服务正常分和服务异常罚分，但未开启“覆盖”的字段继续继承比赛默认值。

### 3.4 Flag 模板与注入

Flag 模板在“比赛管理 → 题目 → 题目规则”中配置，而不是写死在题库模板中。留空时平台按当前默认规则生成：

- 未填写前缀：使用 `flag`；
- 未填写正文模板：使用随机 `[GUID]`；
- 典型结果：`flag{550e8400-e29b-41d4-a716-446655440000}`。

题库模板只配置注入方式。注入命令中必须出现 `${FLAG}`。例如服务从 `/dev/shm/flag` 读取 Flag：

```sh
printf '%s' '${FLAG}' > /dev/shm/flag
```

注意：Runner 直接把 `${FLAG}` 替换到命令后执行 `/bin/sh -c`，不会额外进行 shell 转义。出题人和 Checker 被视为可信代码，但仍应避免把 Flag 拼接进复杂的二次 `eval`、SQL 或不受控路径。推荐只做单一文件写入或调用参数明确的内部脚本。

Container 注入在当前容器中执行；Compose 必须填写接收 Flag 的 `ServiceName`。注入超时默认 30 秒，建议设置为 3～10 秒并保持幂等：同一个 Flag 重复注入不应破坏服务。

服务端应在请求到达时读取最新 Flag，或让注入命令以原子替换方式更新文件。不要只在进程启动时读取一次，否则轮换后仍会泄露旧 Flag。

### 3.5 Checker 合约

Checker 是可信的一次性容器。平台注入以下环境变量：

| 变量 | 含义 |
| --- | --- |
| `NOCTF_TARGET_HOST` | 目标服务在隔离网络中的主机名 |
| `NOCTF_CALLBACK_URL` | 本次检测结果回调地址 |
| `NOCTF_CALLBACK_TOKEN` | 仅绑定本次检测的短期 JWT |

Container 目标的默认 DNS 名为 `target`；Compose 题目必须正确选择目标服务名。Checker 应直接访问题目声明的容器内端口，而不是公网随机端口。

最小 Checker：

```sh
#!/bin/sh
set -eu

if wget -qO /dev/null -T 3 \
  "http://${NOCTF_TARGET_HOST}:8080/health"; then
  state='Up'
else
  state='Down'
fi

wget -qO /dev/null -T 3 \
  --header "Authorization: Bearer ${NOCTF_CALLBACK_TOKEN}" \
  --header 'Content-Type: application/json' \
  --post-data "{\"state\":\"${state}\"}" \
  "${NOCTF_CALLBACK_URL}"
```

合约要求：

- 回调正文只允许 `{"state":"Up"}` 或 `{"state":"Down"}`。
- 同一次 Checker 执行可以多次回调，最后一次有效状态为准。
- 回调成功后 Checker 应以 0 退出；非零退出会记录 Checker 异常，超时会记录 Checker 超时。
- 未得到有效 Up/Down 的平台异常不应错误扣选手服务分。
- Checker 不得把 Token、Flag 或响应正文打印到日志。

### 3.6 轮次、暂停和判定

- 逻辑轮次只按比赛有效运行时间推进。暂停期间时间冻结，不补发“错过的轮次”。
- 当前 Flag 有效区间是半开区间 `[ValidStart, ValidUntil)`；轮换后旧 Flag 立即按过期处理。
- 硬化期随暂停冻结；硬化期内 Checker 仍可运行，但不开放攻击和轮次计分。
- 本队 Flag、错误格式、错误 Flag、过期 Flag均不得分。
- 其他队伍当前唯一 Flag首次提交成功；同一攻击者对同一受害队、题目和轮次的重复提交不重复计分。
- AWD 批量提交没有条数和总字节限制，但每个 Flag 必须为 1～4096 个 UTF-8 字节且不能包含 NUL；一个批次存在非法项时整批拒绝。
- 比赛结束后停止新轮次、Checker、注入和计分，并回收 Runtime、端口和 Runner 容量。

### 3.7 AWD 验收清单

- [ ] 两支以上队伍各自得到独立 Runtime，端口不冲突。
- [ ] 硬化期内只显示本队入口，攻击被拒绝；硬化期后对手入口按配置开放。
- [ ] 每支队伍同一轮 Flag 不同，下一轮全部轮换。
- [ ] 注入重复执行幂等，重置 Runtime 后新 Runtime UUID 获得当前有效 Flag。
- [ ] 自己的 Flag、旧 Flag、错误 Flag、重复 Flag均返回明确结果。
- [ ] 服务正常、主动宕机、Checker 超时、连接失败和恢复均符合预期。
- [ ] 暂停期间不错误轮换或计分，恢复后从正确有效时间继续。
- [ ] 排行榜、GameplayFact、比赛事件和管理端运行状态一致。
- [ ] 结束后容器、网络、端口和容量全部释放。

## 4. AWDP 出题规范

### 4.1 Break 与 Fix 必须分开

AWDP 有两种完全不同的动作：

- **Break**：选手提交 Flag。正确判定后产生 `BreakAttempt`。
- **Fix**：选手上传独立的 `tar.gz` 修复包。平台在一次性目标环境中执行并由 Checker 评估，产生 `FixAttempt`。

Fix 不是 Flag，不能把修复包、修复状态或补丁 ID 塞进 Flag 接口。平台也不会把 Fix 直接应用到长期比赛容器；每次验证都创建可丢弃目标，完成后清理。

### 4.2 推荐出题流程

1. 在题库中新建 `AWDP` 模板，配置可被选手攻击的单 Container 服务。
2. Runtime 选择 `PerTeam`，Flag 来源选择 `PerTeam`，只填写注入环境变量名；
   不手工创建队伍 Flag，不填写内部 UUID。
3. 为 Player Runtime 配置 Docker host port `0` 和 OwnerOnly URL；这就是正式 Break 入口，
   不得另起工作人员维护的外部靶机替代。
4. 配置补丁入口、执行命令、补丁超时、就绪等待时间和上传上限。
5. 配置 Checker 镜像、唯一内部端口和超时。Fix target 会忽略 Player 的公网端口和 URL。
6. 在比赛中添加题目，配置本场 Flag 模板，以及 Break/Fix 分值曲线、次数、
   `RequireBreakBeforeFix` 和罚分覆盖。
7. 准备防御成功、EXP 仍可利用、服务异常、Patch 非零/超时和恶意归档样本。
8. 用完整 E2E 验证 Player Runtime→动态 Flag→Break，以及上传→一次性 Target→Checker→Fix 闭环。

### 4.3 比赛与题目规则

| 字段 | 常用默认 | 含义 |
| --- | ---: | --- |
| 轮次长度 | 300 秒 | Break/Fix 每轮动态结算的逻辑轮次 |
| Break 分值曲线 | 默认曲线 | 当前轮 Break 成功队伍数决定本轮攻击得分 |
| Fix 分值曲线 | 默认曲线 | 当前轮 DefenseSucceeded 队伍数决定本轮防御得分 |
| Flag 错误罚分 | 0 | 错误 Break 提交的可选单次扣分 |
| EXP 利用成功罚分 | 0 | Fix 验证仍可被 EXP 利用时的可选单次扣分 |
| 服务异常罚分 | 0 | Fix 验证中正常服务异常、超时或崩溃时的可选单次扣分 |
| 先 Break 后 Fix | 开启 | 未取得当前要求的 Break 时拒绝 Fix |
| 最大 Break 次数 | 10 | 每队每题接入上限；后端值 `<=0` 表示无限 |
| 最大 Fix 次数 | 10 | 每队每题接入上限；后端值 `<=0` 表示无限 |
| 评测派发 | Automatic | 自动派发；也可选择 ManualBatch |

题目规则可覆盖 Break/Fix 分值曲线、是否要求先 Break、次数、罚分和评测派发。未覆盖的字段继承比赛配置。

同一轮内每个队伍每题每条轨道只取最早 Correct 参与该轮结算。后续合法 Break/Fix 的 GameplayFact 仍保持 `Correct`，不能为了避免重复得分而把它错误改判为 `Duplicate`。

### 4.4 Player Runtime 与一次性 Fix Target

题库只保存一份 Container 技术定义，但平台按用途生成两类不同实例：

- Player Runtime：`Purpose=Player`、有 TeamId，使用 `PerTeam` 动态精确 Flag、host port `0`
  和 OwnerOnly URL；选手 Start/Stop/Reset/Extend 并从自己的实例取得 Break Flag。
- Fix Target：`Purpose=AwdpTarget`、绑定申请它的 Team；申请时没有 Fix GameplayFact，Running 后
  只接受一次 PatchUpload，并在同一事务绑定唯一 Fix GameplayFact；复用干净镜像与唯一内部端口，
  但忽略公开 PortMappings/URL，不向选手提供入口，验证结束后销毁。

共同约束：

- 仅 Container，不支持 Compose 或 OVA；
- `Allocation=PerTeam`、`FlagSource=PerTeam`，只配置环境变量名；
- `InternalPorts` 恰好一个有效端口；
- Player 的公开映射必须指向该端口且 Exposure 为 OwnerOnly；
- 服务能在全新容器中稳定复现漏洞，并能在补丁写入后重新判定；
- 不依赖另一个队伍 Runtime、宿主路径或外网服务。

典型配置参数：

- 目标内部端口：例如 `31337`；
- Player 端口：`31337 -> 0`，URL：`tcp://{HOST}:{PORT}`，OwnerOnly；
- Flag 注入：环境变量 `FLAG`；
- Runtime TTL：建议大于补丁超时、就绪等待和 Checker 超时之和，例如 120 秒；
- 操作超时：例如 60 秒；
- 根文件系统：如果 Fix 需要修改目标文件，不要开启会阻止补丁的只读根文件系统；
- 用户：建议使用数字形式的非 root 用户，并确保补丁目标路径对该用户可写。

### 4.5 Fix 归档规范

只接受 gzip 压缩的 POSIX ustar/pax tar 归档。平台根据内容校验，不依赖上传文件名或 MIME 类型。

默认值：

- 补丁入口：`fix.sh`；
- 补丁命令：`["/bin/sh", "{entrypoint}"]`；
- 补丁超时：60 秒；
- 目标就绪等待：30 秒；
- 最大上传：256 MiB，可配置范围 1 字节～1 GiB。

最小修复脚本：

```sh
#!/bin/sh
set -eu

# 示例：写入目标容器内由服务识别的修复状态。
touch /dev/shm/fixed
```

制作归档：

```sh
tar --format=ustar -czf fix.tar.gz fix.sh
```

归档约束：

- 不会自动剥离顶层目录；若入口是 `fix.sh`，文件必须位于归档根。
- 禁止绝对路径、`..` 路径穿越、符号链接、硬链接、设备文件、重复路径和大小写冲突路径。
- `{entrypoint}` 会作为一个独立 argv 参数替换为容器内路径，不进行 shell 字符串拼接。
- Fix 脚本应幂等；Worker/Wolverine 重投存在不确定执行窗口时，平台会清理旧的一次性目标并从不可变归档重新创建环境。
- 不要在归档文件名、日志或输出中包含 Flag、Token 或凭据。

补丁执行结果：

- 退出码 0：继续运行 Checker；
- 非零退出：`Rejected / AwdpPatchFailed`；
- 超时：`Rejected / AwdpPatchTimeout`。

### 4.6 AWDP Checker 合约

平台注入：

| 变量 | 含义 |
| --- | --- |
| `TARGET_HOST` | 一次性目标在隔离网络中的主机名 |
| `TARGET_READY_TIMEOUT_SECONDS` | 等待目标就绪的秒数 |
| `NOCTF_CALLBACK_URL` | 本次 Fix 结果回调地址 |
| `NOCTF_CALLBACK_TOKEN` | 绑定 GameplayFact、Runtime UUID 和 Checker execution identity 的 JWT |

最小 Checker：

```sh
#!/bin/sh
set -u

target="http://${TARGET_HOST}:8080"
remaining="${TARGET_READY_TIMEOUT_SECONDS}"
exp_succeeded=0
service_healthy=0

while [ "${remaining}" -gt 0 ]; do
  wget -qO- -T 2 "${target}/health" >/dev/null 2>&1 && break
  remaining=$((remaining - 1))
  sleep 1
done

if timeout 10 /checker/exploit.sh "${target}"; then
  exp_succeeded=1
fi

health="$(wget -qO- -T 3 "${target}/health" 2>/dev/null || true)"
if [ "${health}" = "ok" ]; then
  service_healthy=1
fi

if [ "${service_healthy}" -ne 1 ]; then
  outcome='ServiceAbnormal'
elif [ "${exp_succeeded}" -eq 1 ]; then
  outcome='ExploitSucceeded'
else
  outcome='DefenseSucceeded'
fi

wget -qO /dev/null -T 5 \
  --header "Authorization: Bearer ${NOCTF_CALLBACK_TOKEN}" \
  --header 'Content-Type: application/json' \
  --post-data "{\"outcome\":\"${outcome}\"}" \
  "${NOCTF_CALLBACK_URL}"
```

结果映射：

| 回调结果 | GameplayFact | 含义 |
| --- | --- | --- |
| `DefenseSucceeded` | Correct | EXP 未能利用且服务满足规则 |
| `ExploitSucceeded` | Wrong / AwdpExploitSucceeded | EXP 仍可利用 |
| `ServiceAbnormal` | Rejected / AwdpServiceAbnormal | 正常服务交互失败、超时或崩溃 |

Checker 必须把 EXP 作为子进程运行并捕获失败/崩溃，然后继续做正常服务交互；服务异常优先级高于 EXP 结果。只检测“某个文件存在”通常不足以证明真实题目已修复；正式题应实际请求漏洞路径、正常业务路径和禁止绕过路径。

Checker 成功回调后以 0 退出。Runner 级验证超时会判为 `ServiceAbnormal`；Checker 主进程异常退出且没有可信业务结果、存储或 Provider 故障属于平台失败，不应冒充选手错误并消耗其正常计分机会。

### 4.7 Break Flag

- Break 通过普通 Flag 提交进入，但只允许单个 Flag，不支持 AWD 式批量。
- AWDP Runtime 生成/使用的 Flag始终精确比较，不使用正则匹配。
- 目标服务、漏洞读取路径和作者 EXP 必须完整承载 1～4096 UTF-8 字节动态 Flag；不得因固定缓冲区、固定读取长度或截断导致已注入 Flag 无法被提交。
- 本队当前活动 Runtime UUID 的 Flag 才能产生 Break；错误、外队、已停止或旧 Runtime UUID Flag 按模式规则拒绝。
- Reset 为新 Runtime UUID 生成新 Flag，并立刻使旧 Runtime UUID 的 Flag 失效；Flag 不通过 URL、事件或普通日志返回。
- 开启“先 Break 后 Fix”后，未满足当前规则时上传可以保留，但触发 Fix 会稳定拒绝，不创建 Fix GameplayFact，也不会错误消耗补丁。

### 4.8 AWDP 验收清单

- [ ] 正确 Break、错误 Break、重复 Break 和次数上限符合配置。
- [ ] 两支队伍的 Player Runtime/端口/Flag 相互独立，Reset 后旧 Flag 失效。
- [ ] 开启“先 Break 后 Fix”时，未 Break 的 Fix 被明确拒绝。
- [ ] 合法 `tar.gz` 能执行；空包、非 gzip、路径穿越、链接、设备文件、超限包全部拒绝。
- [ ] 补丁入口位于归档预期路径，命令中的 `{entrypoint}` 正确替换。
- [ ] ExploitSucceeded、DefenseSucceeded、ServiceAbnormal 三种 Checker 结果均验证。
- [ ] 补丁非零退出和超时得到明确失败，不出现一直 Processing。
- [ ] Fix 不产生 Break、血榜或普通 Flag 分数。
- [ ] 按轮动态分值在暂停、恢复和跨轮情况下只按当前轮成功队伍数投影。
- [ ] Worker 重投、Runner 重试和回调迟到不会重复执行计分副作用。
- [ ] 一次性目标、Checker、网络、端口和 Runner 容量最终释放。

## 5. 本地完整测试

仓库内置的 E2E 题目位于：

- `backend/tests/NoCTF.E2E/fixtures/awd-runtime`
- `backend/tests/NoCTF.E2E/fixtures/awd-checker`
- `backend/tests/NoCTF.E2E/fixtures/awdp-target`
- `backend/tests/NoCTF.E2E/fixtures/awdp-checker`

它们是最小合约示例，不应直接作为正式题目发布。统一测试入口：

```powershell
dotnet run --file backend/tests/e2e.cs -- --mode awd --suite full
dotnet run --file backend/tests/e2e.cs -- --mode awdp --suite full
```

失败调查时可追加 `--keep-environment` 保留精确测试项目；调查结束后只清理该次测试创建的容器、网络、卷和镜像，禁止执行全局 prune。

出题交付前至少再运行：

```powershell
dotnet build backend/NoCTF.slnx -c Release
dotnet test backend/NoCTF.slnx -c Release
```

如果只修改镜像、Checker 或补丁样例，也必须重新跑对应模式的完整 E2E，不能只用 `curl` 验证服务能打开。

## 6. 常见故障定位

### Runtime 无法启动

检查镜像是否可拉取、镜像 `USER` 是否满足非 root 选项、端口是否声明、资源是否为正数、Compose 是否包含禁用字段，以及 Runtime Provider 是否健康。不要通过写死宿主端口绕过配置错误。

### AWD Flag 注入失败

检查注入命令是否包含精确的 `${FLAG}`、目标路径权限、只读根文件系统、Compose `ServiceName`、超时时间和脚本幂等性。注入命令失败时先修模板，不要手改系统生成的动态 Flag。

### AWD Checker 一直 Unknown

检查 Checker 是否能解析目标主机名、是否访问内部端口、回调是否携带 Bearer Token、JSON 是否严格为 Up/Down，以及 Checker 是否在回调成功后正常退出。不要把 Token 打入 stdout/stderr。

### AWDP Fix 一直 Processing

检查 Worker/Runner/Wolverine 死信、一次性目标 Runtime、补丁执行结果和 Checker 回调。Checker 必须回调并正常退出；当前实现会把非零 Checker 退出收敛为平台失败，不应等待到 Runtime TTL 才显现。

### AWDP Fix 总是 ServiceAbnormal

确认目标服务监听 `0.0.0.0` 而非 `127.0.0.1`，`InternalPorts` 恰好声明真实端口，Checker 使用 `TARGET_HOST`，就绪等待时间覆盖实际启动耗时，并且补丁没有停止主服务。

### 计分与预期不一致

先确认比赛配置和单题规则覆盖的有效值，再检查 GameplayFact 的 kind/result/failureCode、轮次和排行榜投影。配置修改只会触发事件驱动的排行榜失效与重新投影，不会自动重判历史 GameplayFact；需要修正历史结果时使用管理端显式重判/纠正流程，不得修改不可变比赛事件。
