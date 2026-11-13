# AWD 历史记录题模板

HTTP 服务按 StoreId / FlagId 保存真实文件。Checker 通过平台任务 URL 取得清单，执行当前写入和历史读取，再提交完整结构化证据。样例无认证的记录接口是教学漏洞，正式题目需要替换业务与利用方法。

1. 编辑 `statement.md`、`attachment/README.md`、后台表与私有交接单。
2. 在 `starter-kits` 执行 `python tools/verify.py --mode awd`。
3. 分别构建 `awd/private/runtime` 和 `awd/private/checker`，推送正式镜像并填写引用。
4. 执行 `python tools/package.py --mode awd`；公开内容只发布附件包。
5. 正式平台至少验证两队、多轮、跨轮攻击、历史缺失、故障重检和结束清理。

解包独立工作人员包后，在 `awd` 目录执行 `python tools/verify.py --mode awd`。验证覆盖 BusinessChecker 写入、历史读取、删除历史、已注入材料只读和回调失败非零退出。

Runtime 容器重置会丢失当前容器数据；Checker 如实报告历史缺失。RunnerCommand 的样例命令见后台表，不能用当前写入重新制造历史。
