# AWD 后台填写表

新建 AWD 私有模板，复制题面，依次保存 Runtime、Store 和 Checker。实际字段的关系如下。

| 位置 / 字段 | 样例填写值 |
| --- | --- |
| Runtime kind / allocation / flagSource | Container / PerTeam / AwdRotation |
| 服务 name / image | main / `private/runtime` 推送后的完整镜像引用 |
| CPU / 内存 / 内部端口 | 0.1 核 / 64 MiB / 8080 |
| command / arguments / Flag 环境变量 | 留空 |
| 访问入口 | main / 8080 / `http://{HOST}:{PORT}/` / Participants |
| TTL / 操作超时 / egressPolicy | 3600 秒 / 120 秒 / Isolated |
| Store 名称 / 目标服务 | sample / main |
| Store 投放方式 / 历史保存 | BusinessChecker / 支持 |
| Store ID | 由后台创建，保存后记录实际值 |
| Checker image / targetServiceName | `private/checker` 推送后的完整引用 / main |
| Checker command / environment | 留空，使用镜像入口 |
| Checker timeout | 15 秒 |
| 附件策略 | All，上传附件白名单 |

样例 Checker PUT `/flags/{storeId}/{flagId}`，再 GET 同一路径。业务检查 GET `/health`。公开定位字段为上述记录路径；私有读取上下文在本样例为空。

若本题改用 RunnerCommand，Store 选择该方式并指定 main 服务，命令填写：

```sh
mkdir -p "/dev/shm/flags/${STORE_ID}"
printf '%s' "${FLAG}" > "/dev/shm/flags/${STORE_ID}/${FLAG_ID}"
```

保持同一 Checker 的只读材料处理；不要让它补写历史。样例目录使用不带连字符的小写 UUID。

比赛负责人配置轮长、加固、间隔、发行数量、接收与历史窗口、默认 Checker 周期和 ATK / DEF / SLA。题目仅覆盖需要的值。Flag 外观使用安全默认或 `[SECURE]` 模板，在本场比赛及题目规则配置。保存并检查生效规则，完成两队跨轮验收后发布。
