import { describe, expect, test } from 'bun:test'

const page = () => Bun.file(new URL('../app/pages/admin/competitions/[id]/challenges/[ccId].vue', import.meta.url)).text()

describe('admin competition challenge navigation', () => {
  test('places the challenge sections in a sticky right rail on desktop', async () => {
    const source = await page()

    expect(source).toContain('lg:grid-cols-[minmax(0,1fr)_14rem]')
    expect(source).toContain('lg:sticky lg:top-24 lg:col-start-2 lg:row-start-1 lg:flex-col')
    expect(source).toContain('value="general" class="mt-0 lg:col-start-1 lg:row-start-1"')
    expect(source).toContain('value="config" class="mt-0 lg:col-start-1 lg:row-start-1"')
    expect(source).toContain('value="hints" class="mt-0 lg:col-start-1 lg:row-start-1"')
    expect(source).toContain('value="scoring" class="mt-0 lg:col-start-1 lg:row-start-1"')
  })

  test('lists every team and all paged challenge facts before allowing a judge to correct scoring', async () => {
    const source = await page()

    expect(source).toContain('adminListTeams')
    expect(source).toContain('adminListGameplayFacts')
    expect(source).toContain('getLeaderboardEndpoint')
    expect(source).toContain('getScoreboardSchemaEndpoint')
    expect(source).toContain('do {')
    expect(source).toContain('} while (cursor)')
    expect(source).toContain('seenCursors.has(cursor)')
    expect(source).toContain('v-for="row in scoringRows"')
    expect(source).toContain('v-if="canJudge"')
    expect(source).toContain('adminCreateManualAdjustment')
    expect(source).toContain('body: { teamId, competitionChallengeId: ccId, delta: adjustmentDelta.value }')
    expect(source).toContain('if (!teamId || !adjustmentValid.value || adjustmentPending.value) return')

    const catchStart = source.indexOf('catch (requestError)', source.indexOf('async function submitAdjustment'))
    const finallyStart = source.indexOf('finally', catchStart)
    const catchBody = source.slice(catchStart, finallyStart)
    expect(catchBody).not.toContain('adjustmentTarget.value = null')
    expect(catchBody).not.toContain('adjustmentDelta.value = 0')
  })
})
