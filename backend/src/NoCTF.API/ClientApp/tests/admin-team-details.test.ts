import { describe, expect, test } from 'bun:test'

const page = () => Bun.file(new URL('../app/pages/admin/competitions/[id]/teams.vue', import.meta.url)).text()

describe('admin competition team details', () => {
  test('opens a detail sheet from the team name and resolves member profiles through the generated SDK', async () => {
    const source = await page()

    expect(source).toContain('userProfileGet')
    expect(source).toContain('@click="openTeamDetail(t)"')
    expect(source).toContain("$t('队伍详情')")
    expect(source).toContain('member.userId === selectedTeam.captainId')
  })

  test('records one manual score adjustment through the generated SDK and preserves the form on failure', async () => {
    const source = await page()

    expect(source).toContain('adminCreateManualAdjustment')
    expect(source).toContain('adminListCompetitionChallenges')
    expect(source).toContain('@click="openScoreAdjustment(t)"')
    expect(source).toContain('if (!teamId || !scoreAdjustmentValid.value || scoreAdjustmentPending.value) return')
    expect(source).toContain('competitionChallengeId: scoreAdjustmentChallengeId.value')
    expect(source).toContain('delta: scoreAdjustmentDelta.value')
    expect(source).toContain("scoreAdjustmentError.value = parseApiError(requestError, translate('记录得分修正失败')).message")

    const catchStart = source.indexOf('catch (requestError)', source.indexOf('async function submitScoreAdjustment'))
    const finallyStart = source.indexOf('finally', catchStart)
    const catchBody = source.slice(catchStart, finallyStart)
    expect(catchBody).not.toContain("scoreAdjustmentChallengeId.value = ''")
    expect(catchBody).not.toContain('scoreAdjustmentDelta.value = 0')
  })
})
