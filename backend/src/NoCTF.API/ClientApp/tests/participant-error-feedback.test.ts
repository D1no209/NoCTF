import { sourceFile } from './support/feature-source'
import { describe, expect, test } from 'bun:test'

async function source(path: string): Promise<string> {
  return sourceFile(new URL(path, import.meta.url)).text()
}

describe('participant error feedback', () => {
  test('does not disguise failed list requests as empty content', async () => {
    const [home, questions, detail] = await Promise.all([
      source('../app/pages/index.vue'),
      source('../app/pages/competitions/[id]/questions.vue'),
      source('../app/features/challenges/CompetitionChallengeDetail.vue'),
    ])

    expect(home).toContain('<Alert v-if="competitionsError" variant="destructive">')
    expect(home).toContain('@click="loadCompetitions"')
    expect(home).toContain('<PseudoTerminal')
    expect(home).toContain("competitionsLoading.value ? translate('ui.loading')")
    expect(questions).toContain('<Alert v-else-if="challengeLoadError" variant="destructive">')
    expect(questions).toContain('@click="loadChallengeOptions"')
    expect(detail).toContain('v-else-if="attachmentError" variant="destructive"')
    expect(detail).toContain('@click="loadAttachments"')
  })

  test('keeps failed polling visible and recoverable', async () => {
    const [history, submissions, flag, awdp] = await Promise.all([
      source('../app/features/challenges/ChallengeSubmissionHistory.vue'),
      source('../app/pages/competitions/[id]/my/submissions.vue'),
      source('../app/features/challenges/FlagSubmit.vue'),
      source('../app/features/challenges/panels/AwdpPanel.vue'),
    ])

    expect(history).toContain('pendingPollingError')
    expect(history).toContain('pendingPollingTimedOut')
    for (const component of [submissions, flag]) {
      expect(component).toContain('pollingError')
      expect(component).toContain('timedOut')
    }
    expect(flag).toContain("throw parseApiError(error, translate(\"ui.failedToRefreshSubmissionStatus\"))")
    expect(flag).toContain('void refreshOne(id).catch((error) => {')
    expect(history).toContain('@click="startPendingPolling"')
    expect(submissions).toContain('@click="startPolling"')
    expect(submissions).toContain('void refreshPending().catch((requestError) => {')
    expect(awdp).toContain('statePollingTimedOut')
    expect(awdp).toContain('@click="refreshAndPoll"')
  })

  test('reports secondary-data and browser capability failures', async () => {
    const [layout, team, members, submissions, appLayout, live, awdpLive] = await Promise.all([
      source('../app/pages/competitions/[id].vue'),
      source('../app/pages/competitions/[id]/my/team.vue'),
      source('../app/features/teams/TeamMembers.vue'),
      source('../app/pages/competitions/[id]/my/submissions.vue'),
      source('../app/layouts/default.vue'),
      source('../app/pages/competitions/[id]/live.vue'),
      source('../app/pages/competitions/[id]/awdp-live.vue'),
    ])

    expect(layout).toContain('teamLoadError')
    expect(layout).toContain('standingError')
    expect(team).toContain('banCaseError')
    expect(team).toContain("ui.thereAreNoOtherTeamMembersToTransferTheCaptain")
    expect(members).toContain('loadError')
    expect(submissions).toContain('challengeTitlesError')
    expect(appLayout).toContain('platformError')
    expect(appLayout).toContain(':disabled="platformLoading"')
    expect(live).toContain("toast.error(translate(\"ui.theBrowserDeniedFullscreenAccessCheckSitePermissionsOrUse")
    expect(awdpLive).toContain("toast.error(translate(\"ui.theBrowserDeniedFullscreenAccessCheckSitePermissionsOrUse")
  })

  test('loads expensive visualizations only on the routes that need them', async () => {
    const [leaderboard, live, chart, miniChart, trendChart] = await Promise.all([
      source('../app/pages/competitions/[id]/leaderboard.vue'),
      source('../app/pages/competitions/[id]/live.vue'),
      source('../app/utils/echarts.ts'),
      source('../app/components/ui/chart/MiniChart.vue'),
      source('../app/features/leaderboard/ScoreTrendChart.vue'),
    ])

    expect(leaderboard).toContain("<component :is=\"LazyScoreTrendChart\"")
    expect(leaderboard).toContain("<component :is=\"LazyScoreboardTeamDetailDialog\"")
    expect(live).toContain("import('~/lib/live-city-3d')")
    expect(live).not.toContain("import { LiveCityScene } from '~/lib/live-city-3d'")
    expect(chart).toContain('themeColor(property, element ?? undefined)')
    expect(chart).toContain('chartTooltipTheme')
    expect(miniChart).toContain('chartPalette(el.value)')
    expect(miniChart).toContain('width === observedWidth && height === observedHeight')
    expect(miniChart).toContain('resizeFrame = requestAnimationFrame')
    expect(trendChart).toContain('trendChartPalette(el.value)')
    expect(miniChart).toContain('watch(isDark, () => void nextTick(render))')
    expect(trendChart).toContain('watch(isDark, () => void nextTick(scheduleRender))')
  })
})
