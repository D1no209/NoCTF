import { sourceFile } from './support/feature-source'
import { describe, expect, test } from 'bun:test'

const page = () => sourceFile(new URL('../app/pages/admin/competitions/[id]/teams.vue', import.meta.url)).text()

describe('admin competition team details', () => {
  test('opens a detail sheet from the team name and resolves member profiles through the generated SDK', async () => {
    const source = await page()
    const privatePanel = await sourceFile(new URL('../app/features/account/PrivateAccountPanel.vue', import.meta.url)).text()

    expect(source).toContain('userProfileGet')
    expect(source).toContain('@click="openTeamDetail(t)"')
    expect(source).toContain("$t('ui.teamDetails')")
    expect(source).toContain('member.userId === selectedTeam.captainId')
    expect(source).toContain(':show-activities="false"')
    expect(privatePanel).toContain('data.ssoBinding')
    expect(privatePanel).toContain('data.ssoBinding.subject')
  })

  test('records one manual score adjustment through the generated SDK and preserves the form on failure', async () => {
    const source = await page()

    expect(source).toContain('adminCreateManualAdjustment')
    expect(source).toContain('adminListCompetitionChallenges')
    expect(source).toContain('if (!team.id || !canJudge.value) return')
    expect(source).toContain('<Button v-if="canJudge" variant="outline" size="sm"')
    expect(source).toContain('@click="openScoreAdjustment(t)"')
    expect(source).toContain('if (!teamId || !scoreAdjustmentValid.value || scoreAdjustmentPending.value) return')
    expect(source).toContain('competitionChallengeId: scoreAdjustmentChallengeId.value')
    expect(source).toContain('delta: scoreAdjustmentDelta.value')
    expect(source).toContain("scoreAdjustmentError.value = parseApiError(requestError, translate(\"ui.failedToRecordTheScoreAdjustment\")).message")

    const catchStart = source.indexOf('catch (requestError)', source.indexOf('async function submitScoreAdjustment'))
    const finallyStart = source.indexOf('finally', catchStart)
    const catchBody = source.slice(catchStart, finallyStart)
    expect(catchBody).not.toContain("scoreAdjustmentChallengeId.value = ''")
    expect(catchBody).not.toContain('scoreAdjustmentDelta.value = 0')
  })
})
