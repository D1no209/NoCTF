# AWD 交付确认

- 题目名称：历史记录服务，正式交付填写名称。
- 作者、复核人、源码修订：待填写。
- Runtime 与 Checker 镜像完整引用、digest：待填写。
- Store：`sample`，BusinessChecker，`main`，支持历史保存。
- 正常业务、攻击定位和历史读取方法：题面与后台表。
- Hint：`private/HINTS.md`；公开内容：`attachment/`。
- 实际 ChallengeId、CompetitionChallengeId、StoreId、比赛与两队：待填写。
- 本场发行、历史、接收和结算规则版本：待填写。

## 验收记录

- [ ] Docker 当前写入、历史读取、历史丢失和只读任务通过。
- [ ] Checker 回执覆盖完整任务；回调失败非零退出；日志不含材料。
- [ ] 正式平台两队、多轮、跨轮捕获、重复与自己 Flag、窗口过期通过。
- [ ] Ok / Mumble / Offline / Recovering / InternalError 按真实证据验收。
- [ ] 暂停、结束、同轮重检与待裁定处理通过。
- [ ] 模板和比赛 Runtime 已清理，公开附件经过复核。

逐项填写日期、命令、结果、失败和跳过原因，不记录真实 Flag 或内部 JWT。
