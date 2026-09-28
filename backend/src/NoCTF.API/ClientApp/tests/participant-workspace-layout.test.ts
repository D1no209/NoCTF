import { sourceFile } from './support/feature-source'
import { describe, expect, test } from 'bun:test'

const page = (path: string) => sourceFile(new URL(path, import.meta.url)).text()

describe('participant competition workspace layout', () => {
  test('renders public competition pages without a workspace sidebar', async () => {
    const parent = await page('../app/pages/competitions/[id].vue')

    expect(parent).not.toContain("<component :is=\"AppWorkspaceNav\"")
    expect(parent).not.toContain('usesParticipantWorkspace')
    expect(parent).toContain('v-else-if="competition"')
  })

  test('keeps staff-only live screens, teams and events out of the participant navigation', async () => {
    const parent = await page('../app/pages/competitions/[id].vue')
    const adminParent = await page('../app/pages/admin/competitions/[id].vue')

    expect(parent).toContain('isControlScreen')
    expect(parent).toContain('`/competitions/${competitionId.value}/live`')
    expect(parent).not.toContain('`/competitions/${competitionId.value}/live-`')
    expect(parent).not.toContain("label: translate(\"ui.controlScreen\")")
    expect(parent).not.toContain("label: translate(\"ui.3dLiveScreen\")")
    expect(parent).not.toContain("label: translate(\"ui.team\")")
    expect(parent).not.toContain("label: translate(\"ui.activity\")")
    expect(adminParent).toContain("label: translate(\"ui.controlScreen\")")
    expect(adminParent).toContain("label: translate(\"ui.3dLiveScreen\")")
    expect(adminParent).toContain("label: translate(\"ui.teamManagement\")")
    expect(adminParent).toContain("label: translate(\"ui.activity\")")
    expect(adminParent).toContain("middleware: 'platform-admin'")
  })

  test('only enables the challenge navigator when explicitly requested', async () => {
    const shell = await page('../app/features/competition/CompetitionParticipantWorkspace.vue')
    const challenges = await page('../app/pages/competitions/[id]/challenges/[[ccId]].vue')
    const theme = await page('../app/assets/css/main.css')

    expect(shell).toContain('challenge-workspace')
    expect(shell).toContain('v-if="showChallengeNavigator"')
    expect(shell).toContain("'xl:grid-cols-[minmax(0,1fr)_clamp(16rem,20vw,21rem)]'")
    expect(shell).toContain('<slot />')
    expect(shell).toContain("<component :is=\"CompetitionWorkspaceNavigation\"")
    expect(shell).toContain("<component :is=\"CompetitionBroadcastPanel\"")
    expect(shell).toContain('grid-rows-[fit-content(50%)_minmax(0,1fr)]')
    expect(shell).not.toContain('18rem]')
    expect(challenges).toContain('show-challenge-navigator')
    expect(theme).toContain("[data-slot='default-layout-foreground']:has(> main [data-contained-workspace-page])")
    expect(theme).not.toContain("[data-slot='default-layout']:has(> main [data-contained-workspace-page])")
  })

  test('keeps questions and personal pages in the two-column shell but makes the scoreboard standalone', async () => {
    const sources = await Promise.all([
      page('../app/pages/competitions/[id]/questions.vue'),
      page('../app/pages/competitions/[id]/my/team.vue'),
    ])
    const leaderboard = await page('../app/pages/competitions/[id]/leaderboard.vue')
    const writeUpReview = await page('../app/pages/competitions/[id]/writeups.vue')

    for (const source of sources)
      expect(source).toContain("<component :is=\"CompetitionParticipantWorkspace\"")
    expect(leaderboard).not.toContain("<component :is=\"CompetitionParticipantWorkspace\"")
    expect(leaderboard).toContain("$t('ui.backToCompetition')")
    expect(leaderboard).toContain('data-scoreboard-page-scroll')
    expect(leaderboard).toContain('<ScrollSurface as="div" axis="y" data-scoreboard-page-scroll')
    expect(writeUpReview).not.toContain('CompetitionParticipantWorkspace')
    expect(writeUpReview).toContain('data-writeup-review-workspace')
    expect(writeUpReview).toContain("$t('ui.backToCompetition')")
  })

  test('removes the standalone submissions navigation item', async () => {
    const parent = await page('../app/pages/competitions/[id].vue')

    expect(parent).not.toContain('`${base}/my/submissions`')
    expect(parent).not.toContain("label: translate(\"ui.mySubmissions\")")
    expect(parent).not.toContain('FileCheck')
  })

  test('keeps consultation inside the competition navigation group', async () => {
    const parent = await page('../app/pages/competitions/[id].vue')

    expect(parent).not.toContain('label: translate("ui.interaction")')
    expect(parent).toMatch(/label: translate\("ui\.competitions"\)[\s\S]*ui\.leaderboard[\s\S]*ui\.questions[\s\S]*label: translate\("ui\.mine"\)/)
  })

  test('shows the approved participant team rank and points from the live scoreboard snapshot', async () => {
    const parent = await page('../app/pages/competitions/[id].vue')

    expect(parent).toContain('if (!user.value)')
    expect(parent).not.toContain('if (!user.value || hasCompetitionStaffAccess.value)')
    expect(parent).toContain('getLeaderboardEndpoint({')
    expect(parent).toContain("myTeam.value?.registrationStatus !== 'Approved'")
    expect(parent).toContain('myStanding.value = data.teams?.find(team => team.teamId === myTeam.value?.id) ?? null')
    expect(parent).toContain('scoreboardUpdated: () => {')
    expect(parent).toContain('if (!isWriteUpReview.value) void refreshStandingLatest()')
    expect(parent).toContain('onReconnected: () => {')
    expect(parent).toContain("$t('ui.teamRank')")
    expect(parent).toContain("$t('ui.teamPoints')")
    expect(parent).toContain("{{ myStanding.totalScore ?? 0 }} {{ $t('ui.pts2') }}")
    expect(parent).toContain("myStanding.rank ? `#${myStanding.rank}` : '-'")
  })
})
