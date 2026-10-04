# 常见故障排查

先记录时间/时区、Host digest、资源 ID 和错误代码，再定位层级。重复点击、删数据重装或全局 prune 会丢失证据并可能扩大故障。

## Host 无法就绪

```bash
cd /opt/noctf
docker --context YOUR_CONTEXT compose --env-file .env -f docker-compose.yml ps --all
docker --context YOUR_CONTEXT compose --env-file .env -f docker-compose.yml logs --tail=100 noctf
docker --context YOUR_CONTEXT compose --env-file .env -f docker-compose.yml exec -T postgres pg_isready -U noctf -d noctf
```

检查数据库连接、迁移、启动超时、签名/加密 key 格式、NATS 和启用时的 Loki。基础 Docker 观测默认关闭；配置 RequireLoki 后需要依赖真实可用。自动迁移失败时不绕过 readiness。

Kubernetes：

```powershell
pwsh -File deploy/k8s/scripts/Diagnose.ps1 -Context YOUR_CONTEXT
kubectl --context YOUR_CONTEXT -n noctf get pods,pvc
```

优先使用脱敏诊断，再在本机权限范围查看必要日志。PVC Pending 查 Retain PV/class，Pod Pending 查节点标签/配额/requests，ImagePullBackOff 查 Registry DNS/TLS/auth。

## 页面 502、证书或连接失败

检查平台 DNS/443、证书、代理是否加入 `noctf-proxy`、upstream `noctf-web:8080` 和 Host readiness。core Compose 没有宿主端口，访问服务器的 8080 不是标准生产入口。

Nginx 配置变更先 `nginx -t` 再 reload。平台连接正常而 Registry push 失败时，单独检查 `noctf-registry:5000` 对应 HTTPS 路由和上传设置。

## 登录成功但刷新掉线

核对 HTTPS、Secure Cookie、cookie path、精确 Origin（含端口）、CORS 和 forwarded headers。多 API 副本签名/加密 key 保持一致。用户角色/密码/TokenVersion 刚变化会使旧会话失效。

手工模板未添加 `Authentication__RefreshAllowedOrigins__0` 时补齐并重新创建 Host。不要把 Origin 设为 `*`，也不要在生产关闭 Secure。

## 邮件、Cap、Turnstile、SSO 失败

| 功能 | 优先检查 |
| --- | --- |
| 邮件 | SMTP 认证、TLS、发件人、DNS/egress、实际收件与冷却 |
| Cap | 浏览器 Origin/CORS、WASM、后端 URL、site secret、独立 management key |
| Turnstile | key 类型、hostname、后端 siteverify、egress |
| SSO | 本地账号是否绑定、Provider UUID、Issuer/回调、cookie、一次性 flow |

一个未绑定外部身份不能直接自动注册登录。已消费 flow 重放失败时重新发起绑定/登录，不反复刷新旧回调。代理日志不能记录 ticket/code。

## 队伍看不到题目

检查当前注册状态是否 Approved、是否封禁、比赛访问策略、题目发布、CTF 闯关和赛道可见性。刚改成员/队名/赛道会退回 Unregistered，队长需重报。管理员能看见并不能证明 Player 能看见。

## 比赛无法开始

读取 Start Gate 完整错误列表，按配置路径修复。常见为无题、缺 Flag、模式不符、Checker 定义无效、结束时间已到、Runner/额度/存储不可用。失败保持原状态，不直接更新数据库 Status。

## Runtime 无法启动或连接

| 阶段 | 核对 |
| --- | --- |
| 请求拒绝 | 报名/封禁、时间、闯关、队伍额度、人机验证、429 |
| 等待派发 | Worker、JetStream、Runner 在线、Pool/Provider、资源域租约 |
| 镜像拉取 | 宿主 Registry DNS/TLS、Host Docker auth 或 ImagePullSecret |
| 资源创建 | CPU/MiB、PID limit、Pod slots、命名服务和端口预算 |
| 页面 Ready 但不可达 | 最新入口、公网随机端口/NodePort、DNS/NAT/安全组、防火墙 |
| 回收失败 | Provider API、活动 Checker、超时、实例拥有的资源 |

Docker host port 由 Docker 随机分配；重置后读取新地址，不把平台 443 代理当成题目端口。Kubernetes 中只有已验证 PID 限制且有标签的节点可承载适用执行。

## 评测一直处理中

先检查 AWDP 是否手动调度，其次检查 Worker/Runner、JetStream pending、Checker 镜像、内部回调和文件 hash/长度。存储故障与逻辑 Wrong 分开处理，不批量重投所有记录。

任务受理后客户端超时不代表未入库，重试前通过已知 fact ID/列表确认当前状态。永久失败需要按管理流程重判，不直接写 Score。

## 榜单未变化或异常

检查最终事实、资格/封禁、赛道计分与竞争开关、结算模式、冻结/隐藏和投影 Worker。缓存缺失应从关系事实重建，不创建 Dirty 列或手工 Redis 业务 key。

短时异步延迟先等一次刷新。重判/调分/改配置之后核对新权威投影，不对旧公开冻结榜重复补分。

## 文件上传失败或日志为空

413 检查反代和 Endpoint 两层限额；Fix 还检查展开预算与格式。Local 查 bind path/权限，S3 查 bucket、TLS、凭据和 metadata。日志为空查观测是否启用、Loki 是否在线与文档可读，不将 reader 解密错误视为无事件。

## 报障最小资料

保留模式、Git/镜像 revision、精确时间、competition/challenge/team/runtime/fact ID、状态/failure code、步骤和脱敏错误。附少量必要日志，不贴 `.env`、Secret、JWT、Flag 或完整 SQL。
