# LiveSolo 媒体部署

此 overlay 适用于显式启用 LiveSolo 的单机 Linux 安装。主部署默认不启用媒体。
独立 Redis 只供 SFU/Egress；不复用平台缓存，不连接题目、回调或数据库网络。
SFU 控制、Redis 和 Egress 健康端口不发布宿主端口。

先准备独立、容量固定的 spool 挂载。它保存未导入的 HLS/MP4，不能复用上传目录。
必须使用专用固定容量文件系统；准备脚本验证挂载和实际总容量，不创建／格式化磁盘。
Worker 停机时关系化预留与周期检查不能限制 Egress 写盘。挂载耗尽应只让媒体失败，
不能耗尽平台、数据库或系统分区。spool 挂载也必须在重启后保持，否则原始录制可能丢失。

```sh
python3 deploy/docker/live-solo/prepare.py \
  --installation /opt/noctf \
  --spool /srv/noctf-media \
  --media-host media.example.com \
  --rtc-ip 192.0.2.10 \
  --maximum-spool-bytes 17179869184 \
  --media-gid 2001
```

替换示例域名、IP 和专用媒体组 GID，预先配置 spool 组可写且其他用户无权访问。脚本只生成私有配置、检查固定挂载容量并验证 Compose；
不启动服务、不改防火墙、不签发证书。既有媒体配置会被拒绝覆盖。
生成的 config/live-solo 和 env/live-solo 不可提交；密钥与平台 JWT/Runner/上传密钥独立。
spool 供 Egress 的非 root 用户写入，运维应按容器实际 UID/GID 设置专用组权限；
脚本不扩大宿主目录权限。不要把它映射给无关容器。

把 [nginx.conf](nginx.conf) 加到独立媒体域名的 HTTPS server，使用安全访问日志，
不得记录查询字符串或公开 Twirp。配置合法 TLS 证书，开放 RTC TCP 7881/UDP 7882。
公网 RTC 端口和配置中的端口一致；使用端口映射时也必须更新 RTC 通告。
Browser ClientUrl 使用 wss；内部控制/Egress 只走媒体网络。
room.auto_create=false 必须保留，否则被撤销的房间可能被旧授权重建。

```sh
docker compose --project-directory /opt/noctf \
  --env-file /opt/noctf/.env --env-file /opt/noctf/env/live-solo/compose.env \
  -f /opt/noctf/docker-compose.yml -f /opt/noctf/compose.live-solo.yml config --quiet
```

验证后按批准的手动部署流程启动同一组服务。不要输出含密钥的完整渲染配置。
首版仍以比赛功能关闭发布，待真实媒体、恢复和容量验收后再由管理者启用。
默认录制关闭，录像保留默认 30 天，managed archive 仍使用现有 File 存储。
同一 spool 必须对所有接入的 API/Worker 可用；此 overlay 是单机方案，多节点需专门的共享存储部署。

Egress 每实例至少 4 CPU/4 GiB 是供应商建议，不能当成 4 场/16 屏幕容量承诺。
录制关闭／开启两组测试须实测 CPU、内存、网络、存储、裁判视频与状态延迟，按结果扩容。
30 分钟供应商单任务时限补充应用分段，但不替代磁盘硬限额。达到时限会产生终态，
不能将它伪装为完整录像；管理员应核对并按失败恢复流程处理。

参考：[Egress 部署](https://docs.livekit.io/transport/self-hosting/egress/)、
[固定版本 Egress 配置](https://github.com/livekit/egress/blob/v1.15.0/README.md)、
[固定版本 SFU 配置](https://github.com/livekit/livekit/blob/v1.13.9/config-sample.yaml)。
