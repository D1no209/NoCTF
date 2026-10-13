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
- Challenge 定义只选择 Container 命名服务等可移植规格。实际使用 Docker 还是 Kubernetes、使用哪个 Runner Pool，由平台配置统一决定。

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

### 2.2 服务与资源

容器环境统一使用命名服务列表。单容器填写一个名为 `main` 的服务；多容器为各服务填写唯一名称和镜像。资源按服务配置，默认 0.5 核和 512 MiB；CPU 步长为 0.001 核。PID 限制属于平台部署配置，不由出题人填写。容器及 Checker 的安全开关均已删除，运行身份遵循镜像默认用户。

### 2.3 网络、端口和地址

对外入口只在 Runtime 级清单中配置，每项明确引用服务名和容器端口。无需再在服务内声明公开端口，宿主端口由平台随机分配。显示模板可填写 `http://{HOST}:{PORT}/` 或 `nc {HOST} {PORT}`。内部端口仅供 Checker 等平台操作使用。

Docker 单服务复用共享题目网络，多服务才建立实例专属网络和服务别名。Kubernetes 单服务不建立内部发现 Service，多服务通过实例 headless Service 解析服务名称。Docker 的 `Isolated` 不保证禁止公网访问。

完整接口、部署要求和示例见 [容器 Runtime 与命名服务](runtime-services.md)。

## 3. AWD 出题规范

### 3.1 运行模型与制作流程

每队拥有独立 Container Runtime，分配为 `PerTeam`、Flag 来源为 `AwdRotation`。比赛独立推进轮次，按题目、队伍、Store、轮次、发行序号持久化正式 Flag。攻击可提交接收窗口内的历史 Flag，去重身份为攻击队与 FlagId。

1. 新建 AWD 题库模板，配置 1–64 个命名服务，按核/MiB 填写资源。
2. 添加 Runtime 级 Participants 访问入口，选择服务名和容器端口；宿主端口由平台分配。
3. 配置有序 Store，选择 `BusinessChecker` 或 `RunnerCommand`，声明历史保存能力。
4. 配置 Checker 镜像、目标命名服务和超时，按下述任务清单协议实现证据。
5. 用至少两队完成轮换、跨轮捕获、历史丢失、故障恢复、暂停与收官验证。
6. 在比赛中配置默认规则和题目覆盖，交付已填写的 [AWD 模板](challenge-authoring-templates/awd-challenge-template.md)。

Runtime、Checker 和 Store 属于题库；比赛规则、Flag 外观和计分属于比赛或比赛题目。Provider、Runner Pool、PID 限制与安全策略属于部署。不要提交 Compose 或自定义容器权限。

### 3.2 默认规则与窗口

默认轮长 180 秒、加固和间隔 0 秒、Flag 接收 5 轮（含发行轮）、历史窗口跟随接收窗口、每 Store 每轮 1 Flag。ATK/DEF/SLA 预算为 50/100/100，额外处罚关闭；OK 的 SLA 系数 1，RECOVERING 为 0.5，MUMBLE/OFFLINE 为 0，INTERNAL_ERROR 待裁定。Checker 间隔和超时默认 30 秒。

规则面板区分本轮已发行规则、下一轮配置与明确的历史计分修订。nullable 覆盖中 0/false 是显式值。具体五种预设、预算与边界见 [完整规则](awd-rules-2026.zh-CN.md)。

### 3.3 Flag Store 与历史保存

默认 Flag 为专用 HMAC 派生的 `flag{32字符Base64URL主体}`，可用 `[SECURE]` 安全模板改变外观。不可用队伍 UUID、时间戳或低熵随机串代替安全主体。Flag 按精确 UTF-8 字节比较，不 Trim、不改大小写。

`BusinessChecker` 通过业务接口写入本次 `requiresWrite=true` 的 Flag。已有历史项只读，不补造历史；即使当前写入失败，也继续尝试读取任务要求的历史项。真实题目可保存私有登录凭据于 `privateReadContext`，可公开记录 ID 于 `publicLocator`，不得将密码或 Flag 放入公开定位字段。

`RunnerCommand` 将 Flag、FlagId、StoreId 作为独立 shell 参数传入，脚本使用已导出的变量并正确引用：

```sh
mkdir -p "/dev/shm/flags/${STORE_ID}"
printf '%s' "${FLAG}" > "/dev/shm/flags/${STORE_ID}/${FLAG_ID}"
```

命令应幂等，不使用 eval，不覆盖唯一文件破坏历史。投放重试使用同一 Flag；Runtime 重置不能自动回填应由选手保存的旧 Flag。服务启动不清空历史目录。

### 3.4 Checker 任务与回执

| 环境变量 | 用途 |
| --- | --- |
| `NOCTF_TARGET_HOST` | 默认目标服务内部地址 |
| `NOCTF_AWD_TARGETS` | JSON 命名服务地址映射 |
| `NOCTF_AWD_TASK_URL` | 当前尝试的受保护任务材料地址 |
| `NOCTF_AWD_DEADLINE` | 本次执行绝对期限 |
| `NOCTF_CALLBACK_URL` | 当前尝试结果回调地址 |
| `NOCTF_CALLBACK_TOKEN` | 绑定 Runtime、GameplayFact、Attempt 的 JWT |

先以 Bearer JWT GET 任务。响应含 `gameplayFactId`、`attemptId`、`deadline`、`flags`；每项含 `flagId`、`storeId`、`storeName`、`targetServiceName`、`flag`、`historical`、`requiresWrite`、`privateReadContext`、`publicLocator`。只处理这个清单，不猜测其他轮次材料。

回调 JSON 结构：

```json
{
  "connection": { "outcome": "Passed", "reason": "None" },
  "functionality": { "outcome": "Passed", "reason": "None" },
  "flags": [
    {
      "flagId": "aaaaaaaa-0000-0000-0000-000000000001",
      "write": { "outcome": "NotRequired", "reason": "None" },
      "read": { "outcome": "Passed", "reason": "None" },
      "privateReadContext": null,
      "publicLocator": "/records/opaque-record-id"
    }
  ],
  "observedAt": "2026-10-13T00:00:00Z"
}
```

实际回执必须恰好覆盖清单全部 FlagId，无重复；无需写入才可报 `NotRequired`。结果枚举为 `Unknown`、`Passed`、`TargetFailure`、`PlatformFailure`、`NotRequired`；原因使用 OpenAPI 枚举，例如 `ConnectionRefused`、`FunctionalCheckFailed`、`FlagStoreFailed`、`CurrentFlagMissing`、`HistoricalFlagMissing`、`InfrastructureFailure`。

Checker 不上报最终健康、历史责任或计分资格。服务端根据固定清单分类。TCP 建连失败与已连接后的业务失败分开；网络基础设施异常不能一律扣为选手离线。剩余预算不足时保留清单项并报未知，不能省略或默认通过。一次有效回执固定当前尝试证据，重复回调不能用“最后一次获胜”改写结论。

示例 Checker 用 Python 标准库实现，见 [checker.py](challenge-authoring-templates/starter-kits/awd/checker/checker.py)。它只用于样例 HTTP 服务；正式题须实现真实业务探测与可靠故障归因。不要记录 Token、Flag、私有上下文、任务正文或目标响应正文。

### 3.5 健康、执行与结算

- `Ok`：当前连接、功能、当前操作、全部应保存历史读取通过，可同时存在有效捕获。
- `Mumble`：连接成功，但当前业务或当前 Flag 操作失败。
- `Offline`：可靠证据证明无法建立目标连接。
- `Recovering`：当前检查通过，但仍有选手应保存的历史 Flag 缺失。
- `InternalError`：平台、Checker、调度或证据不足导致不可信；保留已知目标故障证据，默认同轮重检，耗尽后待裁定。

待检测、执行中、无法判定、未启用、暂定/最终/豁免是独立维度。新一轮不继承旧健康作为当前结果。默认轮末计划样本及其合法同轮重试用于 SLA；新轮检查不能倒填旧轮。

任务材料读取止于执行期限，原窗口内观察有 30 秒自动回执宽限，更迟证据进入复核。24 小时回执令牌寿命不延长执行或 Flag 接收窗口。

### 3.6 暂停、收官与验收

暂停和轮间间隔冻结有效时钟，暂停发行与新攻击接收，已可靠接收的提交继续处理。默认收官关闭新接收，保留未决检测与结算；高级尾期只接受截止前仍有效的旧发行。最后不完整轮不伪造完整 SLA。

- [ ] 两队 Runtime、地址和 Flag 独立，多 Store/多 Flag 预算不放大。
- [ ] 跨轮首次捕获成功，同队重复不计分，过期、自己、未发行值明确拒绝。
- [ ] 当前写入失败仍读取历史；历史丢失不会被重写成“正常”。
- [ ] OK、MUMBLE、OFFLINE、RECOVERING、INTERNAL_ERROR 与任务、捕获、结算分开展示。
- [ ] 平台漏发与未启用不产生历史责任；服务故障导致的历史缺失保留责任。
- [ ] 暂停、尾期、延迟消息、重复回调与重启接管不重复计分。
- [ ] 主办方预览历史规则变更后提交理由，排行榜解释与公开规则一致。
- [ ] 完成镜像冒烟和真实 API/Worker/Runner 两队 E2E，收官释放资源。

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
- Fix Target：`Purpose=AwdpTarget`、绑定申请它的 Team；申请时没有 Fix GameplayFact，Queued、
  Provisioning 或 Running 时即可接受唯一一次 PatchUpload，并在同一事务绑定唯一 Fix GameplayFact；
  若 Target 尚未 Running，验证会等待启动完成；复用干净镜像与唯一内部端口，
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

Fix 包仍以 `.tar.gz` 提交，内容必须为 gzip 压缩的 tar 归档。支持 GNU Tar、USTAR、PAX 和 V7，
不再要求指定 `--format=ustar`；格式兼容不代表放开链接、特殊文件或不安全路径。

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
tar -czf fix.tar.gz -- fix.sh
```

归档约束：

- 不会自动剥离顶层目录；若入口是 `fix.sh`，文件必须位于归档根。
- 接受 GNU tar 常见的 `./` 根目录条目及 `./fix.sh` 路径；路径规范化后重复的文件仍会拒绝。
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
- 开启“先 Break 后 Fix”后，未满足当前规则时“申请防御”会被明确拒绝，因此不会创建 Target、PatchUpload 或 Fix GameplayFact，也不会消耗尝试次数。

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

检查镜像是否可拉取、默认 `USER` 和目录权限是否正确、命名服务与入口是否一致、资源是否为正数，以及平台 Runtime Provider 是否健康。不要通过写死宿主端口绕过配置错误。

### AWD Flag 注入失败

检查 Store 投放方式、命令中引用的 `${FLAG}`、目标路径权限、命名服务、超时时间和脚本幂等性。注入命令失败时先修模板，不要手改系统生成的动态 Flag。

### AWD Checker 一直无法判定

检查任务材料能否在期限内领取、命名服务是否可达、Bearer Token 是否绑定当前尝试、全部 FlagId 是否有合法证据，以及观察时间是否在原执行窗口内。查看稳定原因、执行状态和待裁定证据后再处理。不要把 Token 打入 stdout/stderr。

### AWDP Fix 一直 Processing

检查 Worker/Runner/Wolverine 死信、一次性目标 Runtime、补丁执行结果和 Checker 回调。Checker 必须回调并正常退出；当前实现会把非零 Checker 退出收敛为平台失败，不应等待到 Runtime TTL 才显现。

### AWDP Fix 总是 ServiceAbnormal

确认目标服务监听 `0.0.0.0` 而非 `127.0.0.1`，`InternalPorts` 恰好声明真实端口，Checker 使用 `TARGET_HOST`，就绪等待时间覆盖实际启动耗时，并且补丁没有停止主服务。

### 计分与预期不一致

先确认比赛配置和单题规则覆盖的有效值，再检查 GameplayFact 的 kind/result/failureCode、轮次和排行榜投影。配置修改只会触发事件驱动的排行榜失效与重新投影，不会自动重判历史 GameplayFact；需要修正历史结果时使用管理端显式重判/纠正流程，不得修改不可变比赛事件。
