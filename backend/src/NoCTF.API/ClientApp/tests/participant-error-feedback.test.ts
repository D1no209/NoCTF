import { describe, expect, test } from 'bun:test'

async function source(path: string): Promise<string> {
  return Bun.file(new URL(path, import.meta.url)).text()
}

describe('participant error feedback', () => {
  test('does not disguise failed list requests as empty content', async () => {
    const [home, questions, detail] = await Promise.all([
      source('../app/pages/index.vue'),
      source('../app/pages/competitions/[id]/questions.vue'),
      source('../app/components/challenges/CompetitionChallengeDetail.vue'),
    ])

    expect(home).toContain('<Alert v-else-if="competitionsError" variant="destructive">')
    expect(home).toContain('@click="loadCompetitions"')
    expect(home).toContain('v-if="competitionsLoading"')
    expect(questions).toContain('<Alert v-else-if="challengeLoadError" variant="destructive">')
    expect(questions).toContain('@click="loadChallengeOptions"')
    expect(detail).toContain('v-else-if="attachmentError" variant="destructive"')
    expect(detail).toContain('@click="loadAttachments"')
  })

  test('keeps failed polling visible and recoverable', async () => {
    const [history, submissions, flag, awdp] = await Promise.all([
      source('../app/components/challenges/ChallengeSubmissionHistory.vue'),
      source('../app/pages/competitions/[id]/my/submissions.vue'),
      source('../app/components/challenges/FlagSubmit.vue'),
      source('../app/components/challenges/panels/AwdpPanel.vue'),
    ])

    expect(history).toContain('pendingPollingError')
    expect(history).toContain('pendingPollingTimedOut')
    for (const component of [submissions, flag]) {
      expect(component).toContain('pollingError')
      expect(component).toContain('timedOut')
    }
    expect(flag).toContain("throw parseApiError(error, translate('刷新提交状态失败'))")
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
      source('../app/components/teams/TeamMembers.vue'),
      source('../app/pages/competitions/[id]/my/submissions.vue'),
      source('../app/layouts/default.vue'),
      source('../app/pages/competitions/[id]/live.vue'),
      source('../app/pages/competitions/[id]/awdp-live.vue'),
    ])

    expect(layout).toContain('teamLoadError')
    expect(layout).toContain('standingError')
    expect(team).toContain('banCaseError')
    expect(team).toContain('队伍中没有其他成员，暂时无法转让队长。')
    expect(members).toContain('loadError')
    expect(submissions).toContain('challengeTitlesError')
    expect(appLayout).toContain('platformError')
    expect(appLayout).toContain(':disabled="platformLoading"')
    expect(live).toContain("toast.error(translate('浏览器拒绝进入全屏")
    expect(awdpLive).toContain("toast.error(translate('浏览器拒绝进入全屏")
  })

  test('loads expensive visualizations only on the routes that need them', async () => {
    const [leaderboard, live, chart, miniChart, trendChart] = await Promise.all([
      source('../app/pages/competitions/[id]/leaderboard.vue'),
      source('../app/pages/competitions/[id]/live.vue'),
      source('../app/utils/echarts.ts'),
      source('../app/components/leaderboard/MiniChart.vue'),
      source('../app/components/leaderboard/ScoreTrendChart.vue'),
    ])

    expect(leaderboard).toContain('<LazyScoreTrendChart')
    expect(leaderboard).toContain('<LazyScoreboardTeamDetailDialog')
    expect(live).toContain("import('~/lib/live-city-3d')")
    expect(live).not.toContain("import { LiveCityScene } from '~/lib/live-city-3d'")
    expect(chart).toContain('getComputedStyle(element ?? document.documentElement)')
    for (const component of [miniChart, trendChart]) {
      expect(component).toContain('chartPalette(el.value)')
      expect(component).toContain('watch(isDark, () => void nextTick(render))')
    }
  })
})
