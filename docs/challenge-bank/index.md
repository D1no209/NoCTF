# 题库管理手册

题库入口位于顶栏“题库管理”，列表 `/admin/challenges`，创建入口 `/admin/challenges/new` 或当前新增弹窗，详情 `/admin/challenges/模板ID`。题库管理能力按平台/模板权限执行，不能套用比赛页面的所有可见性规则。

## 与比赛管理的分工

| 在题库配置 | 在比赛题目配置 |
| --- | --- |
| 模板标题、题面、模板方向、模式 | 比赛内展示名、方向目录和顺序 |
| Runtime 命名服务、Checker、注入与 Patch | 发布、分数、罚分、提示与单题覆盖 |
| 模板附件和静态 Flag | 比赛专属 Flag、提示解锁政策 |
| 模板负责人/管理者、共享可见性 | 比赛 Owner/Manager/Judge/Observer |

先创建/测试模板，再 [引用到比赛](../competition/content/challenges.md)。两个 UUID 不同；模板修改可能影响多个引用方，比赛单题改名则不修改共享题库。

## 阅读顺序

1. [模板创建与编辑](./templates.md)：基本资料、模式、保存分区与共享。
2. [附件与静态 Flag](./attachments-flags.md)：上传、替换、删除/恢复和作用域。
3. [Runtime 与 Checker](./runtime-checkers.md)：服务、资源、入口和各模式约束。
4. [模板测试](./testing.md)：测试环境、注入、续期和清理。
5. [权限与引用管理](./permissions-placement.md)：Owner/Manager、被引用位置和删除限制。

## 上线前核对

用模板管理者保存技术定义，在隔离比赛用 Approved 普通队伍验证实际访问、提交与计分。模板测试不产生正式比赛成绩，也不证明比赛的报名、闯关、TTL 配额全部正确。

应用发布镜像由 Action 提供；题目镜像由出题人按自己的题目构建流程准备。不要把“题目镜像制作”误写成“平台用户必须源码安装 NoCTF”。

## 理论题库与本场材料

[理论题库](./theory.md)使用 `/admin/theory/questions`，维护单选、多选、判断题和版本授权，供个人活动或比赛组卷。它与上面的实践模板分开。

LiveSolo 的 [本场题目池](../competition/modes/live-solo/question-pool.md)支持在比赛内新建材料，材料不进入全局题库。也可投放全局 LiveSolo 模板，首次实际内容编辑后脱离。CTF 转换在全局题库新建 LiveSolo 时执行，再投放到比赛。
