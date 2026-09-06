# AWD 平台配置抄录表

本文件用于把已经在 NoCTF 管理界面中确认的配置随题目一同交付，不是可导入的内部 JSON。不要手写 `schemaVersion`、比赛 ID、队伍 ID、规格 ID、Runner Pool 或 Runtime Provider。

## 题库模板

- 游戏模式：`AWD`
- 分配方式：`PerTeam`
- Flag 来源：`AwdRotation`
- Runtime：Container 或 Compose
- Runtime 镜像：`待填写`
- 对外端口：容器 `8080` / host `0` / Exposure `Participants`
- URL：`http://{HOST}:{PORT}/`
- Flag 注入命令：`printf '%s' '${FLAG}' > /dev/shm/flag`
- Compose ServiceName：Container 不适用；Compose 待填写
- Checker 镜像：`待填写`
- Checker 目标：Container 使用 `target`；Compose 使用目标 service
- Checker 目标端口：`8080`
- Checker 超时：`待填写`

至少一个攻防入口必须使用 `Participants`。Flag 注入超时范围为 1～300 秒。保存后先使用题库测试容器
验证并预热镜像，再加入比赛。

## 比赛题目规则

- Flag 前缀：`待填写`；留空时平台使用 `flag`
- Flag 正文：`待填写`；留空时平台使用随机 `[GUID]`
- 攻击奖励、受害方防守池、Checker 周期、服务分：继承比赛或逐项记录覆盖值

完整字段和边界以包内 `NOCTF-DELIVERY.md` 及平台当前管理界面为准。
