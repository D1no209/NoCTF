# Questionnaire 与独立考试、练习

Questionnaire 是题库 Challenge 的能力。一份问卷包含多道单选、多选或判断小题，小题拥有稳定身份、来源、难度、分类、解析、选项和文件引用。独立活动以用户为参与者。

## 内容与版本

在题库能力编辑器中选择 Questionnaire，编辑整份问卷。题目权限、附件和图片沿用 Challenge 的所有权及管理规则。内容更新创建不可变 QuestionnaireRevision；已发布活动和已经发出的试卷继续使用原有版本。

题库复制创建独立 Challenge、问卷版本、小题和选项身份，保留规范来源。FileId 引用不可变对象，复制操作不重写文件内容。通用复制接口为 `POST /api/v1/admin/challenges/copies`，源题权限在事务内重新检查。

## 活动和作答

- 活动管理位于 `/admin/questionnaires/activities`，参与入口位于 `/questionnaires`。
- `selectionKind=Draw` 按题型、难度及分类抽取小题；`selectionKind=Fixed` 指定题库中的整份问卷。
- 发布固定候选版本、计分规则和次数限制；发卷固定实际分配、小题及选项顺序。
- 发卷、保存答案和交卷分别使用明确的写接口。GET 只读取当前状态。
- 答案仅接受本试卷已分配的选项身份；保存草稿不判分。
- 暂停、恢复、延期、停止发卷、结束及作废均按活动作用范围校验权限和状态。
- 交卷时固定答案并按 Assignment 调用 Questionnaire 判定，重复交卷返回同一结果和首次交卷时间。

每个 QuestionnaireAssignment 关联 Session、真实 Challenge 和内容版本，并拥有一个正式 QuestionnaireAttempt。独立事实的 CompetitionId、CompetitionChallengeId 和 TeamId 均为空。数据库唯一关系防止一个 Assignment 产生重复正式事实。

## 成绩与纠正

成绩从当前逐题判定聚合，支持最高、最近和累计成绩。累计成绩及考试要求有限尝试次数。参与者投影隐藏正确选项、解析和内部判定材料；交卷响应返回成绩。工作人员按阅卷权限读取逐题结果。

纠正流程要求预览影响范围、确认内容和填写原因。纠正更新当前有效结果，保留操作者及既有审计语义，重新计算成绩。试卷作废后不参与成绩聚合。独立活动使用自己的排行榜、通知和导出作用范围。

## 执行与恢复

问卷判定在应用进程内完成。逐题结果、当前能力事实和 Session 交卷状态在同一事务中保存。需要异步恢复的纠正和超时交卷通过持久消息处理；重复消息检查当前状态和请求回执。

运行契约和 HTTP 路由见 [API](api.md)。完整能力矩阵见 [题库能力](challenge-capabilities.md)。
