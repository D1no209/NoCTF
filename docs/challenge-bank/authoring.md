# CTF、AWD、AWDP、KoH 出题与交付

题目作者准备题面、镜像、业务检查和附件；部署人员依据填写表录入后台；赛事负责人配置本场规则并复核。题库模板与比赛题目分别保存，发布前使用普通参赛队伍验收。

## 选择模板

仓库 `specs/challenge-authoring-templates/starter-kits/` 提供四套完整资产。

| 赛制 | 样例 | 平台配置要点 |
| --- | --- | --- |
| CTF | 文件读取漏洞，读取平台注入的完整 Flag | PerTeam，FlagSource=PerTeam，OwnerOnly 入口 |
| AWD | 按 Store / Flag 保存记录，检查当前与历史 | PerTeam，AwdRotation，Participants 入口，Store 与 Checker |
| AWDP | 攻击文件读取漏洞，上传解析器修补 | 单服务、PerTeam、PerTeam Flag、OwnerOnly，Patch 与 Checker |
| KoH | 多队抢占共享控制记录 | Shared，不注入队伍 Flag，Participants 入口和 Control 容器 |

每套目录中，`statement.md` 是给选手的题面，`platform/CONFIGURATION.md` 是逐项填写表，`private/DEPLOYMENT.md` 是部署交接单，`private/HINTS.md` 保存提示，`NOCTF-DELIVERY.md` 记录验收。Runtime、Checker、EXP 和标准修补全部归入 `private/`；`attachment/` 是公开附件白名单。

作者复制完整工作人员包，修改样例题目并完成自测。真实题目仓库应限制访问，目录名本身不提供权限控制。请勿将整个工作人员包上传为附件。

## 作者构建与打包

需要 Python 3.12+、Docker CLI 与 Linux Docker Engine。在源码的 `starter-kits` 目录执行：

```sh
docker context show
python tools/verify.py --mode all
python tools/package.py --mode all
```

`--mode` 可单独选择 ctf、awd、awdp 或 koh。检查通过真实容器执行；失败会非零退出。脚本只清理自己创建的唯一名称资源。

`dist/` 输出每种赛制的工作人员 tar.gz、公开附件 tar.gz、SHA256SUMS，以及 AWDP 内部 Fix 样本。工作人员包自带工具，解包后可在模式目录独立执行相同命令。镜像通过该模式的 `private/runtime` 构建，AWD / AWDP 另构建 `private/checker`。正式交付填写完整仓库引用、digest、源码修订、CPU 架构和构建结果，确认实际 Runner 或节点能拉取。

## 题库与赛事分别填写

| 位置 | 填写内容 |
| --- | --- |
| 题库基本资料 | 赛制、名称、方向、题面、可见性 |
| 题库运行环境 | 命名服务、镜像、资源、Flag 注入位置、访问入口 |
| 题库模式定义 | Store、Checker、Patch 等对应赛制技术字段 |
| 题库附件与 Flags | 公开附件、静态答案及作用域 |
| 比赛配置 | 轮次、轮询、默认分值、罚分、Flag 外观 |
| 比赛题目 | 引用模板、排序、发布时间、单题覆盖、提示 |
| 平台部署 | Provider、Runner Pool、网络、仓库凭据和进程上限 |

每个编辑分区分别保存，再创建测试实例。模板 ID 与比赛题目 ID 不同，玩家行为属于实际 CompetitionChallengeId。镜像和定义变更用于后续启动或重置，活动目标不会自动替换。

## 容器与网络

Container 可包含 1–64 个命名服务，每服务填写唯一小写名称、镜像、CPU 核数与内存 MiB。默认 0.5 核、512 MiB；0.1 核对应 100 millicores，内存 64 表示 64 MiB。平台将全部服务需求相加。服务之间通过名称通信，自行处理依赖尚未启动的情况。

访问入口在 Runtime 级添加服务名、容器端口、显示模板和可见范围。例如 main、8080、`http://{HOST}:{PORT}/`、OwnerOnly。无需填写固定宿主端口。Docker 使用服务自己的随机端口映射；Kubernetes 使用 NodePort。

镜像自行声明默认 USER 并准备写入目录。不要配置宿主挂载、Docker socket、特权容器、Compose YAML 或容器安全开关。PID 限制由部署统一设置。

Docker 单服务共享题目 bridge，不保证不同队伍网络互相隔离，也不保证禁止公网出站。Kubernetes 的隔离依赖实际 CNI 与 NetworkPolicy。需要限制选手对控制端点的访问时，单独监听私有端口并验收网络策略。

## CTF

静态题关闭 Runtime，配置精确或正则答案，审核附件。多附件随机题为每份候选维护关联 Flag，首次实际下载固定本队附件。

容器 Flag 题配置 PerTeam 与 Flag 环境变量名，至少选择一个注入服务。平台传入真实 Flag，镜像和 EXP 保留完整 UTF-8 字节。普通 Flag 题不配置 Checker。

CTF 的每队 Flag 首次生成后保持不变，重置复用原值。重置仍产生新 Runtime UUID，地址需重新读取。正式验收包含两队作用域、Wrong / Correct、重置、题目时间、计分、血奖、提示和闯关。

OVA 题使用预置静态答案，交付 OVA URL、SHA-256、资源与 Guest Agent 要求；平台不注入容器环境变量。CTF PatchVerification 是独立实验交互，应按实验规范启用和验收。

## AWD

目标按队分配，FlagSource 使用 AwdRotation。Runtime 公开 Participants 入口，Store 选择目标服务和 BusinessChecker / RunnerCommand。

BusinessChecker 按本次固定清单写入当前材料并读取历史；RunnerCommand 在指定服务注入，Checker 读取已有值。按 StoreId / FlagId 保存，使用 `printf '%s'` 保留原文，避免覆盖唯一文件造成历史丢失。

Checker 从 `NOCTF_AWD_TASK_URL` 领取任务，以内部 JWT 授权。清单包含本次 Flag、身份、写入要求、历史标记及读取上下文。它分别报告连接、业务、每条 Flag 写入和读取证据；覆盖全部清单，即使当前写入失败仍读取历史。材料、Token 和目标响应不打印到日志。

最终 Ok / Mumble / Offline / Recovering / InternalError 由平台计算，攻击捕获独立。正式验收至少包含两队多轮、跨轮接收、历史缺失、平台故障、同轮重检、暂停和结束结算。

## AWDP

恰好一个服务，一个内部端口；全部公开入口为 OwnerOnly，并引用这个服务及端口。攻击 Runtime 注入绑定当前 UUID 的 Flag，重置后更换答案。Break 提交 Flag，Fix 上传 tar.gz。

Fix 包根目录直接包含入口和替换文件。后台 Patch 命令逐项填 `/bin/sh`、`{entrypoint}`；平台执行 `/noctf/fix/fix.sh`。脚本以自身位置定位随包文件，以目标默认用户写入。平台拒绝路径穿越、链接、特殊文件和超限归档。

一次性 Fix 目标与长期攻击目标独立，移除公开入口，不注入攻击 Flag。Patch 完成后 Checker 使用 `TARGET_HOST` 访问目标，分别检查正常业务和漏洞。回调只报告 DefenseSucceeded / ExploitSucceeded / ServiceAbnormal。Checker 非零退出和回调失败保留平台失败结果。

打开“向 Checker 提供 Fix 包”后，`/noctf/fix` 提供规范化选手输入副本；目标运行后生成的文件不在其中。Checker 不执行持有回调凭据环境中的选手脚本，检查真实目标时使用业务接口。

正式验收包含正确修补、仍可利用、业务损坏、Patch 非零 / 超时、Checker 失败、非法包、两队新旧攻击 Flag、次数与前置 Break、结果查询和回收。

## KoH

所有队伍共用一个 Shared Hill。目标不注入各队 Flag 列表；各队从平台取得自己的 Control Flag，再通过题目协议占领目标。

配置至少一个 Participants 公开入口及独立内部 Control URL。平台 GET 控制端点，正文直接返回完整 Flag：1–4096 UTF-8 字节，无 NUL，精确匹配。不要返回 JSON、修剪空白或追加换行。2xx 无有效匹配表示无人控制，非 2xx / 超时记录失败，不能沿用上一控制者继续得分。

比赛及单题配置轮询秒数和每次控制分。Paused 保持资源并停止轮询，Resume 恢复，Finished 回收；延迟和暂停不补造观测。KoH 无 Flag 提交、Fix 上传和 Checker 镜像。

## 发布验收

1. 作者完成 Docker 检查，记录输出与镜像修订。
2. 复核人员解包附件，核对 SHA-256，检查无私有源码、EXP、Checker 和正确修补。
3. 保存题库定义，在 TemplateTest 验证镜像、端口、注入及清理。
4. 隔离比赛使用两支 Approved 队伍投放实际题目，执行本赛制清单。
5. 验证排行榜、题目时间、暂停恢复、结束和资源回收，记录失败及未验收项。
6. 确认后台表与实际保存值一致后发布。

模板 Docker 检查不涵盖平台 JWT、权限、持久化和计分；正式题目仍需实际比赛验收。平台仓库的四模式 E2E 入口为 `backend/tests/e2e.cs`，详情见源码中的 `specs/testing.md`。
