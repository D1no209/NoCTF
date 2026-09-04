import { describe, expect, test } from 'bun:test'
import { competitionChallengeConflictMessage } from '../app/lib/competition-challenge-conflict'
import { statusErrorMessage } from '../app/utils/api-error'

describe('competition challenge custom title', () => {
  test('allows an optional competition-only title while adding from the question bank', async () => {
    const page = await Bun.file(
      new URL('../app/pages/admin/competitions/[id]/challenges/index.vue', import.meta.url),
    ).text()

    expect(page).toContain('v-model="newCustomTitle"')
    expect(page).toContain('customTitle: newCustomTitle.value.trim() || null')
    expect(page).toContain("$t('留空时使用题库模板标题')")
    expect(page).toContain("$t('只修改本场比赛中的展示名称,不会更改题库模板')")
  })

  test('allows restoring the template fallback from challenge settings', async () => {
    const page = await Bun.file(
      new URL('../app/pages/admin/competitions/[id]/challenges/[ccId].vue', import.meta.url),
    ).text()

    expect(page).toContain("editCustomTitle.value = c.customTitle ?? ''")
    expect(page).toContain('customTitle: editCustomTitle.value.trim() || null')
    expect(page).toContain('maxlength="160"')
  })

  test('maps each add conflict from the generated response code', () => {
    expect(competitionChallengeConflictMessage({ code: 'ChallengeTemplateConflict' }))
      .toBe('该题目已加入当前比赛,请编辑已有题目。')
    expect(competitionChallengeConflictMessage({ code: 'ChallengeOrderConflict' }))
      .toBe('该顺序已被其他题目占用,请更换顺序。')
    expect(competitionChallengeConflictMessage({ code: 'ResourceIdConflict' }))
      .toBe('题目资源标识冲突,请重新添加。')
    expect(competitionChallengeConflictMessage({ code: 'LifecycleStateConflict' }))
      .toBeUndefined()
  })

  test('keeps generic HTTP conflicts separate from typed add conflicts', () => {
    expect(statusErrorMessage(409)).toBe('资源状态已发生变化,请刷新页面获取最新状态后重试')
  })

  test('keeps the add dialog and its inputs after an SDK error', async () => {
    const page = await Bun.file(
      new URL('../app/pages/admin/competitions/[id]/challenges/index.vue', import.meta.url),
    ).text()
    const addHandler = page.slice(page.indexOf('async function addChallenge()'), page.indexOf('// ---- Delete / restore ----'))

    expect(addHandler).toContain('competitionChallengeConflictMessage(error)')
    expect(addHandler).not.toContain("selectedTemplateId.value = ''")
    expect(addHandler).not.toContain("newCustomTitle.value = ''")
    expect(addHandler.match(/addOpen\.value = false/g)).toHaveLength(1)
  })
})
