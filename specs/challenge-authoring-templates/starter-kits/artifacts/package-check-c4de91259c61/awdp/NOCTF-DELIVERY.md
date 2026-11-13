# AWDP 交付确认

- 题目名称：文档柜修补，正式交付填写名称。
- 作者、复核人、源码修订：待填写。
- Runtime 与 Checker 镜像完整引用、digest：待填写。
- Runtime：单服务 `main`，内部端口 8080，OwnerOnly 攻击入口，FLAG 环境注入。
- Patch：`fix.sh`；命令 `/bin/sh`、`{entrypoint}`。
- Checker Fix 输入：本题按后台表选择；网络判定支持开启与关闭。
- 标准修补与失败样本：`private/fixes/`，只限内部验收。
- 公开修补骨架：`attachment/patch-template/`。
- 实际模板、比赛题目、测试比赛与两队：待填写。

## 验收记录

- [ ] Docker 验证攻击 Flag 原文与长度边界、实际路径穿越通过。
- [ ] 合法修补、漏洞仍存在、业务损坏、Patch 非零 / 超时通过。
- [ ] Checker Fix 输入开关两种情况、回调失败通过。
- [ ] 正式平台新旧攻击 Runtime Flag、两队作用域、Break 通过。
- [ ] 上传包校验、一次性目标、Patch、Checker、Fix 与结果查询通过。
- [ ] 当前次数、前置 Break、计分、暂停恢复、结束和资源回收通过。
- [ ] 选手附件无 EXP、标准答案、Checker 或私有探测文件。

记录实际命令、日期、结果和未完成项；禁止写入 Flag、Token 或仓库凭据。
