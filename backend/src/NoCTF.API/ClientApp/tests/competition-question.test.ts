import { describe, expect, test } from 'bun:test'
import type {
  NoCtfapiEndpointsChallengesQuestionsCompetitionQuestionFailureCode,
  NoCtfapiEndpointsChallengesQuestionsCompetitionQuestionFailureResponse,
  NoCtfapiEndpointsChallengesQuestionsCompetitionQuestionResponse,
} from '../app/api'
import {
  competitionQuestionErrorMessage,
  competitionQuestionRoleLabel,
  competitionQuestionUnreadCount,
  mergeCompetitionQuestions,
} from '../app/lib/competition-question'
import { useCursorPagination } from '../app/composables/useCursorPagination'

type Question = NoCtfapiEndpointsChallengesQuestionsCompetitionQuestionResponse

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

describe('competition question cursor collection', () => {
  const baseTime = Date.parse('2026-08-11T12:00:00.000Z')
  const question = (index: number, revision = 0): Question => ({
    id: `00000000-0000-0000-0000-${index.toString().padStart(12, '0')}`,
    title: `Question ${index}`,
    revision,
    updatedAt: new Date(baseTime - index * 1000).toISOString(),
  })

  test('merges 175 paged roots, deep links, refreshes, and replies without duplicates or loss', () => {
    const serverItems = Array.from({ length: 175 }, (_, index) => question(index))
    let visible = mergeCompetitionQuestions([], [serverItems[125]!])

    for (let offset = 0; offset < serverItems.length; offset += 50) {
      visible = mergeCompetitionQuestions(visible, serverItems.slice(offset, offset + 50))
    }

    expect(visible).toHaveLength(175)
    expect(new Set(visible.map(item => item.id)).size).toBe(175)
    expect(new Set(visible.map(item => item.id))).toEqual(new Set(serverItems.map(item => item.id)))

    const created: Question = {
      ...question(999),
      id: 'ffffffff-ffff-ffff-ffff-ffffffffffff',
      title: 'Created during refresh',
      updatedAt: new Date(baseTime + 1000).toISOString(),
    }
    const replied: Question = {
      ...serverItems[160],
      title: 'Updated by reply',
      revision: 1,
      updatedAt: new Date(baseTime + 2000).toISOString(),
    }
    visible = mergeCompetitionQuestions(visible, [created, replied, ...serverItems.slice(0, 50)])

    expect(visible).toHaveLength(176)
    expect(new Set(visible.map(item => item.id)).size).toBe(176)
    expect(visible[0]?.id).toBe(replied.id)
    expect(visible.find(item => item.id === replied.id)?.title).toBe('Updated by reply')

    visible = mergeCompetitionQuestions(visible, [serverItems[160]!])
    expect(visible.find(item => item.id === replied.id)?.title).toBe('Updated by reply')
  })

  test('passes each signed cursor through all four pages of a 175 root result', async () => {
    const serverItems = Array.from({ length: 175 }, (_, index) => question(index))
    const cursors: Array<string | null> = []
    const pagination = useCursorPagination<Question>(async (cursor) => {
      cursors.push(cursor)
      const offset = cursor ? Number.parseInt(cursor.slice('cursor-'.length), 10) : 0
      const nextOffset = offset + 50
      return {
        items: serverItems.slice(offset, nextOffset),
        nextCursor: nextOffset < serverItems.length ? `cursor-${nextOffset}` : null,
      }
    })

    do {
      await pagination.loadMore()
    } while (pagination.hasMore.value)

    expect(cursors).toEqual([null, 'cursor-50', 'cursor-100', 'cursor-150'])
    expect(pagination.items.value).toHaveLength(175)
    expect(new Set(pagination.items.value.map(item => item.id)).size).toBe(175)
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
    expect(page).toContain('applyDetailQuestion(data)')
    expect(page).toContain('if (markAsRead) markRead(fresh)')
    expect(page).toContain('participantMessagesRemaining')
  })

  test('uses generated signed cursors and exposes load-more without truncating older threads', async () => {
    const page = await Bun.file(
      new URL('../app/pages/competitions/[id]/questions.vue', import.meta.url),
    ).text()

    expect(page).toContain('useCursorPagination<Question>(fetchQuestionPage)')
    expect(page).toContain('query: { cursor, limit: 50 }')
    expect(page).toContain('nextCursor: data.nextCursor ?? null')
    expect(page).toContain('mergeCompetitionQuestions(questions.value, page.items ?? [])')
    expect(page).toContain('const refreshList = createTrailingRefresh(async () =>')
    expect(page).toContain('const refreshSelectedDetail = createTrailingRefresh(async () =>')
    expect(page).toContain('competitionEventChanged: () => void refreshFromServer()')
    expect(page).toContain('v-if="hasMore"')
    expect(page).toContain('@click="loadMoreQuestions"')
    expect(page).not.toContain('query: { limit: 100 }')
  })

  test('keeps the thread list narrow and gives the conversation the remaining width', async () => {
    const page = await Bun.file(
      new URL('../app/pages/competitions/[id]/questions.vue', import.meta.url),
    ).text()

    expect(page).toContain("lg:grid-cols-[minmax(17rem,22rem)_minmax(0,1fr)]")
    expect(page).toContain("xl:grid-cols-[22rem_minmax(0,1fr)]")
    expect(page).toContain('min-h-[36rem]')
    expect(page).toContain('rows="7"')
  })
})
