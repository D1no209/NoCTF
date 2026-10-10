# LiveSolo 媒体部署与磁盘边界

该功能是普通平台安装之外的**可选单机 Linux 媒体 overlay**。平台应用仍用 Action 完整镜像，不要求用户源码编译 NoCTF。从同版本部署配置包的 `deploy/docker/live-solo` 准备媒体。

## 前置条件

独立 LiveKit SFU、Egress 和其专用 Redis，媒体密钥与平台 JWT/Runner key 分开。媒体 Redis 不用作 NoCTF 业务协调，媒体服务不加入题目、callback 或 PostgreSQL 网络。

准备真实媒体 HTTPS/WSS 域名、可信 TLS、RTC 可达 IP 和一致端口，默认 RTC TCP 7881 / UDP 7882。控制 API、Twirp、Redis 和健康端口保持私有，不通过公开反代放开。

## 固定容量 spool

spool 必须是专用固定容量文件系统，不复用 uploads 或系统/数据库分区。管理员预先配置持久挂载、容量和私有组写权限；准备脚本不会创建/格式化磁盘或扩大权限。

同一文件系统也要容纳 Egress `/home/egress/tmp` 暂存；只限制最终 `/out` 不够。重启后挂载仍必须存在，磁盘满应限制在媒体故障域内。

## 准备配置

从配置包解压目录执行，替换示例域名、地址、GID 和容量：

```bash
cd /opt/noctf-release
python3 deploy/docker/live-solo/prepare.py \
  --installation /opt/noctf \
  --spool /srv/noctf-media \
  --media-host media.example.com \
  --rtc-ip 192.0.2.10 \
  --maximum-spool-bytes 17179869184 \
  --media-gid 2001
```

脚本生成私有配置、检查挂载和 Compose，不启动、不改防火墙、不发证书，拒绝覆盖已有媒体配置。先准备实际非 root UID/GID 访问，再让相应 Host/Worker 读取同一 spool。

按包内 Nginx 示例配置媒体独立 TLS server，访问日志不记录查询串。保留 room.auto_create=false，避免被撤销的房间被旧 token 重建。

## 校验与启动

```bash
docker --context YOUR_CONTEXT compose --project-directory /opt/noctf \
  --env-file /opt/noctf/.env --env-file /opt/noctf/env/live-solo/compose.env \
  -f /opt/noctf/docker-compose.yml -f /opt/noctf/compose.live-solo.yml config --quiet
```

复核后用同一组文件执行受控 `up -d`，如主部署有 RustFS/监控 overlay 一并保留。不要输出含密钥的完整渲染配置。

默认 Worker all 包含独立 livesolo-media 队列；显式队列选择的部署必须安排该队列，默认并发 4。比赛 Control 时钟不应被媒体任务排队阻塞。Egress 启动独立超时默认 30 秒、允许 5–120 秒，超时结果未知必须核对，不能自动重复 Start。

## 上线限制

当前 overlay 是单机方案，多节点须设计共享持久存储和恢复。容量预留覆盖三份副本，仍不能替代磁盘硬限制。4 场/16 路合成轨道和 50 HTTP 观众测试不承诺任意服务器或 50 解码浏览器的容量。

完整步骤以对应版本 [部署 README](https://github.com/D1no209/NoCTF/blob/HEAD/deploy/docker/live-solo/README.md)为准。正式开赛前完成 [真实验收](./readiness.md)，不因服务启动成功自动启用所有比赛。
