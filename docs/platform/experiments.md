# 实验功能与 LiveSolo 视频策略

入口：平台设置 → 实验功能，`/admin/platform/experiments`。当前提供 CTF Patch Verification 开关及独立 LiveSolo 视频资源策略，两个分区各自保存。

LiveSolo 视频宽高、FPS、码率的默认/范围、生效时机和实际验收见 [平台视频策略](./video-policy.md)。修改它不重启 Match，不扩大普通四赛制的媒体依赖。

## 开关的意义

开启后相应题库编辑/交互能力可以被当前前端识别，用于 CTF 修复验证题。它仍是 CTF，不能因此创建 AWDP 的另一种游戏模式。

Patch Verification 通过修复 archive 和 Checker 判定，与普通 FlagSubmission 交互不同。此类目标不注入普通 Flag，容器/内部端口/Patch/Checker 仍要符合当前定义约束。

## 启用步骤

1. 确认当前 Host、前端和 Runner 支持此功能。
2. 在隔离比赛准备可验证题目及合法/失败/坏包样本。
3. 开启并保存，重新读取当前值和公开平台能力。
4. 在题库选择对应 CTF 交互，保存各技术分区。
5. 通过模板测试和普通 Approved 队伍验收。
6. 再为正式比赛发布题目，并说明修复包格式和计分。

页面未修改时保存不可用是正常的 dirty 检查，不是权限故障。保存平台开关不会替你补齐模板定义、发布比赛题目或执行旧包。

## 关闭前

先盘点引用模板和进行中的题目，安排依赖方处理，再关闭/保存。不要把开关当作自动删除旧题目、清历史分或强制回收全部 Runtime 的操作；实际动作按对应业务能力进行。

修复包、尝试限额、资源、存储和展开预算说明见 [Runtime 与 Checker](../challenge-bank/runtime-checkers.md)、[评测](../competition/judging/submissions.md)。
