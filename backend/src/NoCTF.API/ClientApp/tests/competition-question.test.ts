import { describe, expect, test } from 'bun:test'
import type {
  NoCtfapiEndpointsChallengesQuestionsCompetitionQuestionFailureCode,
  NoCtfapiEndpointsChallengesQuestionsCompetitionQuestionFailureResponse,
} from '../app/api'
import {
  competitionQuestionErrorMessage,
  competitionQuestionRoleLabel,
  competitionQuestionUnreadCount,
} from '../app/lib/competition-question'

const expectedFailureText = {
  InvalidRequest: '咨询内容或请求参数无效',
  SpamRejected: '咨询内容疑似重复或无效文本',
  CompetitionNotAcceptingQuestions: '当前比赛状态不允许创建咨询',
  TeamNotEligible: '当前队伍尚不具备发起咨询的资格',
  InvalidChallengeReference: '关联题目无效、未发布或不属于当前比赛',
  TeamActiveQuestionLimitReached: '本队已有 7 个活跃咨询',
  ParticipantMessageLimitReached: '工作人员回复前最多连续发送 4 条消息',
  RevisionConflict: '咨询已被其他成员更新',
  InvalidTransition: '当前咨询状态不允许执行此操作',
  QuestionClosed: '该咨询已关闭',
} satisfies Record<NoCtfapiEndpointsChallengesQuestionsCompetitionQuestionFailureCode, string>

describe('competition question errors', () => {
  for (const [code, expected] of Object.entries(expectedFailureText)) {
    test(`shows a Chinese explanation for ${code}`, () => {
      const payload: NoCtfapiEndpointsChallengesQuestionsCompetitionQuestionFailureResponse = {
        code: code as NoCtfapiEndpointsChallengesQuestionsCompetitionQuestionFailureCode,
        limit: code === 'TeamActiveQuestionLimitReached'
          ? 7
          : code === 'ParticipantMessageLimitReached'
            ? 4
            : null,
      }
      expect(competitionQuestionErrorMessage(payload, '提交失败')).toContain(expected)
    })
  }
})

describe('competition question presentation state', () => {
  test('distinguishes every participant and handler role', () => {
    expect(competitionQuestionRoleLabel.Participant).toBe('选手')
    expect(competitionQuestionRoleLabel.Judge).toBe('裁判')
    expect(competitionQuestionRoleLabel.ChallengeOwner).toBe('题目所有者')
    expect(competitionQuestionRoleLabel.CompetitionManager).toBe('比赛管理员')
    expect(competitionQuestionRoleLabel.PlatformAdministrator).toBe('平台管理员')
  })

  test('counts revisions after the thread was last viewed', () => {
    expect(competitionQuestionUnreadCount(6, 4, 'Judge', 'Asker')).toBe(2)
    expect(competitionQuestionUnreadCount(6, 6, 'Judge', 'Asker')).toBe(0)
  })

  test('surfaces one initial unread handler reply for an unvisited participant thread', () => {
    expect(competitionQuestionUnreadCount(3, undefined, 'Judge', 'Asker')).toBe(1)
    expect(competitionQuestionUnreadCount(3, undefined, 'Participant', 'Asker')).toBe(0)
    expect(competitionQuestionUnreadCount(3, undefined, 'Judge', 'Handler')).toBe(0)
  })
})

describe('competition question page wiring', () => {
  test('keeps failed drafts and guards duplicate reply submissions', async () => {
    const page = await Bun.file(
      new URL('../app/pages/competitions/[id]/questions.vue', import.meta.url),
    ).text()

    expect(page).toContain('if (replyPending.value || !reply.value.trim() || !detail.value?.canReply) return')
    expect(page).toContain('competitionQuestionErrorMessage(error, translate("发送失败"))')
    expect(page).toContain('finally {')
    expect(page).toContain('replyPending.value = false')
    expect(page).toContain('upsertQuestion(data)')
    expect(page).toContain('markRead(data)')
    expect(page).toContain('participantMessagesRemaining')
  })
})
