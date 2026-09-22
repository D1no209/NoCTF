# GitOps 联调与上线检查

本轮以 [NoCTF-Challenge-Template](https://github.com/D1no209/NoCTF-Challenge-Template)
的 `25718936d1a32a1a8c5f73a74143e350d726c7aa` 为参考，适配当前 NoCTF 契约。
平台不拉取 Git、不构建镜像；仓库 Action 使用普通 Bot JWT 调用普通管理 API。

## 只使用现有接口

GitOps 不新增接口、同步表或服务端仓库解析。客户端通过既有 auth/me、比赛详情、题库详情、
附件/Flag/Hint 列表等 GET 接口读取身份、权限与当前状态，然后使用原有 CRUD 完成收敛。

`apply --dry-run` 只进行本地 Manifest 校验、镜像 digest 解析和上述只读检查，对 NoCTF 不发送
POST/PUT/DELETE。它不是服务端全量配置验证，也不保证后续写入成功。模式配置、活动 Runtime、
权限及生命周期由实际 CRUD 写入重新验证；不能用试写后回滚模拟 dry-run。

## 模板仓库必须同步更新

旧仓库的 `revision` / `expectedRevision` 和独立 `baseScore` 不属于当前 API。
不要为兼容旧脚本把这些字段重新加回平台：

| 内容 | 当前约定 |
| --- | --- |
| CTF Definition / Rules | Definition 为 schemaVersion 3，Rules 为 schemaVersion 2；分值在 rules.scoreCurve |
| AWD Definition / Rules | schemaVersion 4；分值在 attackPoints、serviceHealthyPoints 等规则中 |
| AWDP Definition / Rules | schemaVersion 4；Break 与 Fix 分别配置曲线 |
| KoH Definition / Rules | schemaVersion 1；controlPointsPerInterval |
| 比赛内题目更新 | 必须显式发送 customTitle（可为 null）、order、isPublished |
| Flag 模板 | flagTemplate 属于比赛题目规则，不放在题库 Definition 中 |
| AWDP Checker Fix 输入 | checkerFixInput 属于 Definition，不能放入 Rules |
| 恢复资源 | POST 空 JSON，保持 application/json |

仓库 Manifest 使用稳定 UUID。附件内容、文件名或 ContentType 改变时必须分配新附件 UUID。
选手附件只允许引用 `attachments/`，不能把 `solution/` 或运行源码误上传；禁止路径越界和符号链接。

## 操作顺序

1. 平台管理员创建 Organizer Bot，并签发普通 Access JWT；不要使用 Administrator Bot。
2. 将 Bot 加入比赛 ManagerIds。接管已有题库模板时，还须独立授予模板 Manager 权限。
3. 在仓库配置 `NOCTF_API_URL` 与 Secret `NOCTF_BOT_TOKEN`，完成初始化 PR。
4. 在可信环境运行 `dotnet run --file .github/scripts/repository.cs -- self-test`。
5. 镜像准备完毕后运行 `apply --dry-run`，检查本地配置、权限和附件身份；服务端语义在写入时验证。
6. 审核期望状态后再执行 `apply`；出现活动 Runtime 或生命周期冲突时，停止并报告，不绕过平台规则。

`competition.yml.challenges` 是这场比赛的完整题目集合，附件、Flag 和 Hint 也是完整集合。
应用前必须把要保留的资源纳入 Manifest；仓库之外的 UI 编辑可能被下次 Apply 覆盖。
删除题库模板必须提供明确的 Git base/head 删除证据；不根据目录缺失猜测模板删除。

## 重试与安全

- GET 支持有界传输重试和完整 cursor 分页，重复 cursor 会终止，避免死循环。
- 不盲目重试写入；创建响应丢失后按稳定 UUID 重读，只有内容/父资源匹配才视为成功。
- 业务 409、权限错误不自动重试；中途失败可重新 Apply，同步从当前状态继续。
- 题目交换顺序先使用空闲临时序号，再应用目标顺序；恢复时还需避让墓碑的旧序号，并在最终排序
  前逐一恢复并腾空旧槽位，允许同槽位多个墓碑及中断重跑，不新增修订字段。
- 已发布题目及 Shared 模板在更新过程中保持原状态；新建资源在依赖完成前不发布。提示内容与时间
  先做本地校验，后续写入失败也不将已有题目留在下线状态。
- 镜像引用必须是唯一的 build/external 映射；Container、Checker、Compose serviceImages 同样检查。
  仅 external 的 sha256 digest 合法，不能用普通字符串绕过。平台本身仍允许普通镜像 tag。
- source hash 使用 `noctf-source-v2` 域、长度前缀、文件摘要与 Git 文件模式，避免路径/内容拼接歧义。
  首次升级须全量重建仓库镜像；不删除旧 tag/digest，避免影响既有运行环境。
- 不记录 JWT、Flag 或任意 API 错误响应正文；CI 错误保留方法、资源路径、HTTP 状态与稳定错误码。
- 仅修改题面或附件不重建无关镜像；镜像内容、Dockerfile 或构建配置变化才触发对应构建。
- PR 不读取 Bot Token；只有可信 main 的 Apply 阶段可以访问平台。

## 回归测试

`GitOpsTemplateFixtureTests` 验证模板脚本真实物化的八组配置。
Fixture 由模板仓库 `contract-fixtures --output <path>` 生成，只含明确不可运行的测试镜像占位 digest，
不是可导入生产的题目包。

设置 `NOCTF_GITOPS_TEMPLATE_ROOT` 为审核后的模板 checkout，设置
`NOCTF_REQUIRE_DOCKER_INTEGRATION=true`，运行 `GitOpsRepositoryHttpTests`：
它创建隔离 PostgreSQL、真实 JWT 与本地 HTTP API，调用模板脚本验证 dry-run 零写请求、首次导入、
无操作重跑、已发布题目更新失败保持发布、顺序交换、附件/Flag/Hint 恢复、旧序号被占用时的题目
恢复及恢复后中断重跑、附件不可变性与权限拒绝。
测试消息 Outbox 为隔离替身；此测试验证 HTTP/认证/关系数据，不宣称证明 NATS/Wolverine 的持久投递。

本轮不需要数据库 Migration，也不依赖新接口。此前试加的配置预检接口及 SDK 已撤回。
