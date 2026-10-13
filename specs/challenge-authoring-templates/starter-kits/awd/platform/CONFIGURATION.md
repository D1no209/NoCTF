# AWD 样例平台配置

这是管理界面配置抄录表，不是直接导入 JSON。正式交付补全 `NOCTF-DELIVERY.md`。

- Runtime：Container，PerTeam，AwdRotation。
- 服务 `main`：构建 `runtime/` 的镜像，0.1 核、64 MiB，内部端口 8080。
- Runtime 访问入口：`main:8080`，Participants，`http://{HOST}:{PORT}/`。Docker 自动随机发布宿主端口。
- Store `sample`：编辑器生成稳定 ID，BusinessChecker，目标服务 `main`，支持历史保存。
- Checker：构建 `checker/` 的镜像，目标服务 `main`，建议 15 秒执行预算。
- 比赛默认：轮长 180 秒，Flag 接收 5 轮，历史窗口联动，1 Store/1 Flag；ATK/DEF/SLA=50/100/100。
- Flag 模板：留空使用安全默认，或正文 `[SECURE]`；不用 `[GUID]` 替代 AWD 安全派生。

样例 Checker 从任务清单取得 StoreId/FlagId，以 PUT `/flags/{storeId}/{flagId}` 写入当前 Flag，以 GET 同路径读取全部要求的历史。样例为无认证存储，故意可直接读取，仅用于协议验证；正式题替换业务与漏洞流程。

若改为 RunnerCommand，Store 注入命令使用：

```sh
mkdir -p "/dev/shm/flags/${STORE_ID}"
printf '%s' "${FLAG}" > "/dev/shm/flags/${STORE_ID}/${FLAG_ID}"
```

样例路径中的 StoreId/FlagId 使用不带连字符的小写 UUID，与 Runner 导出的变量一致。

Checker 会依据 `requiresWrite` 自动只读该投放方式的材料。服务启动不清空目录；正式 Runtime 重置后历史丢失必须如实检测。不要配置 Compose、固定宿主端口、作者安全策略、Provider 或 Runner Pool。
