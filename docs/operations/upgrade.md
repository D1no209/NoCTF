# 升级与回退

NoCTF 启动可自动应用数据库迁移。升级不是只换镜像：需要审核 schema、消息契约、前端和 Runtime 定义是否兼容，并保存同一恢复点。

## 升级前

1. 记录当前 Host digest、源码 revision、Provider/Pool、域名与配置。
2. 在隔离副本验证新镜像、迁移、登录、文件与目标比赛模式。
3. 审查新旧 schema 和 JetStream 消息契约能否混跑。
4. 核对生成的 Wolverine Handler、完整前端和正确依赖版本已在镜像中。
5. 通知维护窗口，决定是否暂停或结束比赛及如何处理题目 Runtime。
6. 创建 [一致恢复点](./backup.md)，外部保管原配置和密钥。

不兼容更新需停写全部旧 Api/Worker/Runner，处理活动 Runtime 和待判消息，不能让新旧角色长期混跑。不同版本的实际 cutover 要按发布说明执行，手册不提供旧 schema 自动转换承诺。

## Docker 更新

先从 [新成功 Action](../installation/images.md)取得完整镜像和同次部署配置包，解压到独立的新版本目录。读取 `deployment.json` 的新 digest，在安装 `.env` 的 `NOCTF_PLATFORM_IMAGE` 修改为该地址。保留项目名、数据目录和密钥；修改前备份受限配置。

```bash
docker --context YOUR_CONTEXT pull YOUR_REGISTRY/noctf@sha256:YOUR_NEW_DIGEST
cd /opt/noctf
docker --context YOUR_CONTEXT compose --env-file .env -f docker-compose.yml config --quiet
docker --context YOUR_CONTEXT compose --env-file .env -f docker-compose.yml up -d noctf
docker --context YOUR_CONTEXT compose --env-file .env -f docker-compose.yml exec -T noctf /usr/local/bin/noctf-healthcheck
```

S3/监控要加与原安装相同 overlay。向导维护的安装可更新其受控参数再用 `check/deploy` 助手，不在 installation.json 与 `.env` 中保留互相矛盾的镜像值。

启动后的迁移可能不可逆。readiness 成功后重新验收管理员会话、附件、提交、榜单、通知和 Runtime；有失败时保持维护状态，不直接恢复选手写入。

## Kubernetes 更新

沿用原安装方式：向导生成配置的部署继续使用配置助手；手工 production overlay 继续使用审核过的 overlay 与显式 context。

```powershell
pwsh -File deploy/k8s/scripts/Deploy.ps1 -Context YOUR_CONTEXT -Environment production -Image YOUR_REGISTRY/noctf@sha256:YOUR_NEW_DIGEST
pwsh -File deploy/k8s/scripts/Verify.ps1 -Context YOUR_CONTEXT -Environment production
```

普通升级省略 `-Initialize`。配置变化触发 checksum rollout；Secret 变化需受控 rollout。确认 schema 不兼容时先停旧角色，而不是凭 Deployment 默认滚动策略混跑。

## 切换到独立代理网络

当前模板统一使用运维管理的 `noctf-proxy`，主平台、Registry、Grafana 和 Cap 的 Compose network key 为 `proxy`。

已有部署切换流程：

1. 创建/核验目标 `noctf-proxy` bridge。
2. 将实际容器化代理接入该网络，并核对 DNS aliases。
3. 在自己的安装目录应用新的 core/monitoring/Cap 模板，保留 env、数据与密钥。
4. 更新可信代理 CIDR 为新网络真实范围。
5. 分栈重新创建相关容器，使网络与环境变量持久生效。
6. 检查平台、Registry、Grafana、Cap 的 HTTPS 和后端可达性。
7. 验证完成后按原网络所有者的流程脱离旧连接，不删除仍承载其他服务的网络。

仓库修改不会自动更改现有生产安装目录，更不会自动迁移线上网络。项目名和持久目录不要随代理网络变化一并重命名。

## 回退判断

如果仅部署失败且数据库/数据未变化，可以按已验证兼容性回退镜像和配置。若已经应用 schema 或写入新格式数据，退旧镜像并不等于退 schema，应停写并恢复对应 PostgreSQL、文件/S3、JetStream 和密钥的同一恢复点。

保留恢复点镜像，先在隔离目标恢复验证再切入口。新版本产生的写入如何处理需按赛事运维决策明确，不能自动丢弃。

## 维护结束

记录通过/失败/未运行的验收、升级时间、digest、迁移结果和异常处置。确认只有一套目标接受正式写入，再通知恢复比赛。
