import { describe, expect, test } from 'bun:test'

const page = (path: string) => Bun.file(new URL(path, import.meta.url)).text()

describe('participant competition workspace layout', () => {
  test('renders public competition pages without the legacy workspace sidebar', async () => {
    const parent = await page('../app/pages/competitions/[id].vue')

    expect(parent).not.toContain('<AppWorkspaceNav')
    expect(parent).not.toContain('usesParticipantWorkspace')
    expect(parent).toContain('v-else-if="competition"')
  })

  test('keeps staff-only live screens, teams and events out of the participant navigation', async () => {
    const parent = await page('../app/pages/competitions/[id].vue')
    const adminParent = await page('../app/pages/admin/competitions/[id].vue')

    expect(parent).toContain('isControlScreen')
    expect(parent).not.toContain("label: translate(\"中控大屏\")")
    expect(parent).not.toContain("label: translate(\"3D 大屏\")")
    expect(parent).not.toContain("label: translate(\"队伍\")")
    expect(parent).not.toContain("label: translate(\"动态\")")
    expect(adminParent).toContain("label: translate(\"中控大屏\")")
    expect(adminParent).toContain("label: translate(\"3D 大屏\")")
    expect(adminParent).toContain("label: translate(\"队伍管理\")")
    expect(adminParent).toContain("label: translate(\"动态\")")
  })

  test('only enables the challenge navigator when explicitly requested', async () => {
    const shell = await page('../app/components/competition/CompetitionParticipantWorkspace.vue')
    const challenges = await page('../app/pages/competitions/[id]/challenges/index.vue')

    expect(shell).toContain('xl:grid-cols-[15rem_minmax(0,1fr)_19rem]')
    expect(shell).toContain('v-if="showChallengeNavigator"')
    expect(shell).toContain("'xl:grid-cols-[minmax(0,1fr)_19rem]'")
    expect(shell).toContain('<slot />')
    expect(shell).toContain('<CompetitionWorkspaceNavigation')
    expect(shell).toContain('<CompetitionBroadcastPanel')
    expect(challenges).toContain('show-challenge-navigator')
  })

  test('keeps questions and personal pages in the two-column shell but makes the scoreboard standalone', async () => {
    const sources = await Promise.all([
      page('../app/pages/competitions/[id]/questions.vue'),
      page('../app/pages/competitions/[id]/my/team.vue'),
      page('../app/pages/competitions/[id]/my/submissions.vue'),
    ])
    const leaderboard = await page('../app/pages/competitions/[id]/leaderboard.vue')

    for (const source of sources)
      expect(source).toContain('<CompetitionParticipantWorkspace')
    expect(leaderboard).not.toContain('<CompetitionParticipantWorkspace')
    expect(leaderboard).toContain("$t('返回比赛')")
  })
})
