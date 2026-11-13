# CTF、AWD、AWDP、KoH 出题与部署

面向题目作者、题库管理人员和赛事部署人员。可运行资产集中在 [四种赛制模板](challenge-authoring-templates/README.md)。先选择赛制，再复制对应目录，填写题面与配置表，构建镜像并完成验收。

## 选择题目形态

| 赛制 | 目标与访问范围 | Flag 与交互 | 自动检查 |
| --- | --- | --- | --- |
| CTF | 静态附件，或按队启动 Container / OVA | 静态答案或每队动态 Flag；提交单个 Flag | Flag 自动判定；普通 Flag 题无需 Checker |
| AWD | 每队长期目标，Participants 入口 | 按 Store、轮次发行 Flag，攻击其他队并维护本队业务和历史数据 | 独立 Checker 领取任务并提交结构化证据 |
| AWDP | 每队攻击目标，OwnerOnly 入口；每次 Fix 使用独立一次性目标 | Break 提交当前攻击 Runtime 的 Flag；Fix 上传 tar.gz | Patch 执行后由一次性 Checker 检查业务与漏洞 |
| KoH | 所有队共用 Hill，Participants 入口 | 本队 Control Flag 写入目标；平台轮询控制端点 | 平台 GET 内部 Control URL，精确匹配队伍 Flag |

样例服务故意包含可利用的漏洞，用来展示真实读写和修复行为。正式题目需要替换业务、漏洞、EXP 和对应断言，并按相同平台契约交付。

## 配置归属

| 位置 | 作者应交付的内容 |
| --- | --- |
| 题库 `Challenge` | 题名、方向、题面、附件、Runtime 命名服务、Checker、Store 注入方式、Patch 入口 |
| 比赛 `Competition` | 赛制规则、轮次或轮询周期、计分、默认 Flag 模板 |
| 比赛题目 `CompetitionChallenge` | 引用模板、展示名称、顺序、发布与时间、规则覆盖、提示 |
| 部署配置 | Docker / Kubernetes、Runner Pool、镜像仓库凭据、网络、PID 限制、平台密钥 |

后台采用结构化编辑器。`platform/CONFIGURATION.md` 是人工填写表；服务名、镜像、参数分别录入对应输入框。UUID 由平台创建，部署人员记录实际资源 ID。Flag 外观由比赛及比赛题目配置；题库只配置注入位置。

## 文件与交付

每个模板包含以下目录。`private` 是作者仓库中的分类，仓库访问权限仍需单独设置。

| 文件或目录 | 用途 | 发布对象 |
| --- | --- | --- |
| `statement.md` | 可复制到后台的选手题面 | 选手 |
| `attachment/` | 经审核的公开说明、协议或选手修补骨架 | 选手附件白名单 |
| `private/runtime/` | 目标源码和 Dockerfile | 作者与部署人员 |
| `private/checker/` | AWD / AWDP Checker | 作者与部署人员 |
| `private/DEPLOYMENT.md` | 镜像版本、构建、回收和验收交接 | 作者与部署人员 |
| `private/HINTS.md` | 提示草稿及公开条件 | 赛事管理人员 |
| `private/fixes/` | AWDP 标准修补及失败样本 | 内部验收 |
| `platform/CONFIGURATION.md` | 后台逐项填写表 | 部署人员 |
| `NOCTF-DELIVERY.md` | 交付确认与真实平台验收记录 | 作者与复核人员 |

在 `starter-kits` 执行：

```sh
python tools/verify.py --mode all
python tools/package.py --mode all
```

需要 Python 3.12+、Docker CLI 和可用的 Linux Docker Engine。脚本使用当前明确选择的 Docker context，测试前执行 `docker context show` 确认环境。每次测试创建唯一命名的网络、容器与镜像，并只清理这些资源。

生成目录为 `starter-kits/dist/`，已被 Git 忽略。每种赛制生成工作人员完整包、选手附件包和 SHA-256 清单。工作人员包内自带工具，可独立解包验证。发布附件只使用 `*-attachments.tar.gz`；完整包包含解题材料。

## 容器部署契约

一个 Container Runtime 包含 1–64 个命名服务。服务名使用小写 DNS label，最长 63 字符；服务之间以名称通信。每项填写镜像、CPU 核数、内存 MiB，按需填写启动命令、参数、环境变量、内部端口及 Flag 环境变量名。默认资源为每服务 0.5 核、512 MiB；CPU 精度为 0.001 核。平台按所有服务之和预留容量。

Runtime 级 `urlBindings` 是公开入口清单。每项填写 `serviceName`、`containerPort`、`urlTemplate`、`exposure`；例如 `main`、`8080`、`http://{HOST}:{PORT}/`。TCP 服务可填写 `nc {HOST} {PORT}`。内部端口单独用于 Checker 等平台操作。Docker 直接为服务发布随机宿主端口，Kubernetes 为公开入口分配 NodePort。

AWDP 与 CTF PatchVerification 要求恰好一个服务和一个内部端口。AWDP 的每个公开入口都必须是 OwnerOnly，并指向这个服务及内部端口。KoH 单独配置 `isControlCheck=true` 的 HTTP(S) 控制入口；它使用内部地址，不增加公网端口。

镜像必须由实际 Runner 或 Kubernetes 节点成功拉取。镜像 tag 可用，交付时同时记录 digest 和源码修订，保证可重建。修改模板影响后续启动或重置；现有 Runtime 保持当前内容，重置生成新的 Runtime UUID，访问地址需要重新读取。

镜像使用自身默认 `USER`。样例使用 `10001:10001`，需要写入的目录预先赋权。服务监听 `0.0.0.0`。启动阶段包含全部依赖，运行阶段不安装软件。作者无权配置 privileged、宿主挂载、Docker socket、容器安全开关或 PID 上限。题库定义不接收 Compose、volumes、depends_on、replicas 或任意 Kubernetes 清单。

Docker 单服务复用部署级题目 bridge，共享网络上的实例可能互通；多服务建立实例独占 bridge 和服务别名。固定 internal callback bridge 用于 Checker 与平台通信。Docker `Isolated` 不保证禁止公网出站。需要跨实例和出网隔离时，运维应部署实际执行 NetworkPolicy 的 Kubernetes，并验证 DNS、策略和节点配置。完整行为见 [Runtime 命名服务](runtime-services.md)。

OVA 适用于 CTF 和 KoH：作者交付可下载 OVA、SHA-256、资源需求、Guest Agent 与访问端口。CTF OVA 使用静态 Flag；平台不向虚拟机注入环境变量。受控 Libvirt 节点验收见 [测试规范](testing.md)。本次四套可运行模板均使用 Container。

## Flag 内容

Flag 使用精确 UTF-8 字节，1–4096 字节，无 NUL。服务、EXP 和检查器必须保留大小写、空格、换行和非 ASCII 字符；禁止用 `echo`、`strip()` 或固定 32 字符读取破坏内容。

- CTF 每队动态 Flag 在首次生成后持久保存，重置复用原值。静态题在模板或比赛题目作用域维护精确或正则答案；随机附件需要为各候选正确配置关联 Flag。
- AWDP 攻击 Flag 绑定当前 Runtime UUID；重置产生新 Flag，旧值失效。一次性 Fix 目标不注入攻击 Flag，样例在该目标生成独立探测内容。
- AWD 按题目、队伍、Store、轮次和发行序号派生安全 Flag，历史保留与接收窗口由规则决定。使用 `[SECURE]` 安全模板。
- KoH 各队获得自己的 Control Flag，队伍把它写入共享 Hill；平台不向 Hill 注入整份队伍 Flag 列表。

原始 Flag、JWT、邀请令牌、任务正文和私有读取凭据不进入日志、题面或交接表。

## 从作者测试到比赛验收

1. 填写题面、后台表、Hint 与交接单，审核附件白名单。
2. 运行模板 Docker 检查并构建交付包；从归档解包后再次运行，检查源文件、执行权限和 SHA-256。
3. 将镜像推送到 Runner 可访问的仓库，填写正式引用并验证拉取。
4. 保存题库所有定义后创建 TemplateTest Runtime，检查端口、注入、访问及资源回收。TemplateTest 属于 `ChallengeId`。
5. 在隔离比赛引用模板，记录实际 `CompetitionChallengeId`，准备至少两支 Approved 队伍。
6. 按对应赛制清单验证真实玩家权限、Flag、Fix、轮次或轮询、排行榜、暂停、恢复、结束和清理。

模板 Docker 检查通过只能证明样例服务、Checker 和归档行为。它不验证平台 JWT 声明、数据库约束、队伍资格或计分。仓库平台验收命令从根目录执行：

```sh
dotnet run --file backend/tests/e2e.cs -- --mode ctf --suite full
dotnet run --file backend/tests/e2e.cs -- --mode awd --suite full
dotnet run --file backend/tests/e2e.cs -- --mode awdp --suite full
dotnet run --file backend/tests/e2e.cs -- --mode koh --suite full
```

这些命令使用仓库自身的 E2E 题目，验证平台流程；正式题目的发布验收还须在隔离比赛投放作者交付的镜像。将两类结果分别记入交接单。Kubernetes、Libvirt、公开网络和多节点故障按部署条件单独验收。

## 各赛制契约

- [CTF 出题、静态答案与动态容器](challenge-authoring-templates/ctf-challenge-template.md)
- [AWD Store、轮换、历史保存与 Checker 证据](challenge-authoring-templates/awd-challenge-template.md)
- [AWDP 攻击 Flag、Fix 归档与 Checker](challenge-authoring-templates/awdp-challenge-template.md)
- [KoH 共享 Hill、Control URL 与控制权](challenge-authoring-templates/koh-challenge-template.md)
