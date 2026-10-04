import { sourceFile } from './support/feature-source'
import { describe, expect, test } from 'bun:test'

const page = () => sourceFile(new URL('../app/pages/admin/competitions/[id]/teams.vue', import.meta.url)).text()

describe('admin competition team details', () => {
  test('opens a detail sheet from the team name and resolves member profiles through the generated SDK', async () => {
    const source = await page()
    const privatePanel = await sourceFile(new URL('../app/features/account/PrivateAccountPanel.vue', import.meta.url)).text()

    expect(source).toMatch(/api\.api\.v1\.users\.byUserId\([^)]*\)\.get\(/)
    expect(source).toContain('@click="openTeamDetail(t)"')
    expect(source).toContain("$t('common.label.teamDetails')")
    expect(source).toContain('member.userId === selectedTeam.captainId')
    expect(source).toContain(':show-activities="false"')
    expect(source).toContain('<Accordion')
    expect(source).toContain('type="single"')
    expect(source).toContain('collapsible')
    expect(source).toContain('v-if="expandedMemberId === member.userId')
    expect(source).toContain('@update:model-value="setExpandedMember"')
    expect(source).toMatch(/api\.api\.v1\.admin\.competitions\.byCompetitionId\([^)]*\)\.teams\.byTeamId\([^)]*\)\.invitationToken\.get\(/)
    expect(source).toContain('v-if="canWrite" class="text-muted-foreground">{{ $t(\'competitions.label.invitationCode\') }}')
    expect(source).toContain(':model-value="teamInvitationToken" readonly')
    expect(source).toContain('@click="copyTeamInvitation"')
    expect(source).toContain('if (!canWrite.value || !team?.id) return')
    expect(privatePanel).toContain('data.ssoBinding')
    expect(privatePanel).toContain('data.ssoBinding.subject')
  })

  test('records one manual score adjustment through the generated SDK and preserves the form on failure', async () => {
    const source = await page()

    expect(source).toMatch(/api\.api\.v1\.admin\.competitions\.byCompetitionId\([^)]*\)\.gameplayFacts\.manualAdjustments\.post\(/)
    expect(source).toMatch(/api\.api\.v1\.admin\.competitions\.byCompetitionId\([^)]*\)\.challenges\.get\(/)
    expect(source).toContain('if (!team.id || !canJudge.value) return')
    expect(source).toContain('<Button v-if="canJudge" variant="outline" size="sm"')
    expect(source).toContain('@click="openScoreAdjustment(t)"')
    expect(source).toContain('if (!teamId || !scoreAdjustmentValid.value || scoreAdjustmentPending.value) return')
    expect(source).toContain('competitionChallengeId: scoreAdjustmentChallengeId.value')
    expect(source).toContain('delta: scoreAdjustmentDelta.value')
    expect(source).toContain("scoreAdjustmentError.value = parseApiError(requestError, describeMessage(\"administration.competitionsBy.error.recordScoreAdjustmentFailed\")).displayMessage")

    const catchStart = source.indexOf('catch (requestError)', source.indexOf('async function submitScoreAdjustment'))
    const finallyStart = source.indexOf('finally', catchStart)
    const catchBody = source.slice(catchStart, finallyStart)
    expect(catchBody).not.toContain("scoreAdjustmentChallengeId.value = ''")
    expect(catchBody).not.toContain('scoreAdjustmentDelta.value = 0')
  })
})
