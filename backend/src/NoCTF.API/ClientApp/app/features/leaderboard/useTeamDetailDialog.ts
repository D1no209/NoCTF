import { markRaw, toRefs } from 'vue'
import type { Ref } from 'vue'
import { Award, Medal, ShieldCheck, Swords, Target, Users } from '@lucide/vue'
import { getTeamEndpoint } from '../../api'
import type { NoCtfapiEndpointsTeamsTeamResponse } from '../../api'
import type { echarts } from '../../utils/echarts'
import { medalRankClass, normalizeChallengeKey } from './types'
import type { ChallengeInfo, TrendSeries } from './types'
import ScoreTrendChartComponent from './ScoreTrendChart.vue'

/** Owns state, effects and commands for TeamDetailDialog. */
export function useTeamDetailDialog(props: Readonly<{
  competitionId: string
  mode?: string | null
  entry: {
    teamId?: string
    teamName?: string
    rank?: number
    score?: number
    solveCount?: number
    attackScore?: number
    defenseScore?: number
    penaltyScore?: number
    cells?: Array<{ competitionChallengeId?: string; attackScore?: number; defenseScore?: number }>
  } | null
  series?: TrendSeries | null
  challenges: ChallengeInfo[]
  rangeStart?: string | null
  rangeEnd?: string | null
}>,
open: Ref<boolean>) {
  const isAwdp = computed(() => props.mode === 'Awdp')

  const team = ref<NoCtfapiEndpointsTeamsTeamResponse | null>(null)

  watch([open, () => props.entry?.teamId], async ([isOpen, teamId]) => {
    if (!isOpen || !teamId) return
    team.value = null
    const { data, error } = await getTeamEndpoint({ path: { competitionId: props.competitionId, teamId } })
    if (!error && data) team.value = data
  })

  const challengeTitle = computed(() => {
    const map = new Map<string, string>()
    for (const challenge of props.challenges) {
      if (challenge.competitionChallengeId) {
        map.set(normalizeChallengeKey(challenge.competitionChallengeId), challenge.title ?? translate("ui.challenge"))
      }
    }
    return (id?: string) => map.get(normalizeChallengeKey(id)) ?? translate("ui.challenge")
  })

  const solves = computed(() =>
    [...(props.series?.solves ?? [])].sort((a, b) => String(b.at).localeCompare(String(a.at))),
  )

  const radarOption = computed<echarts.EChartsCoreOption>(() => {
    const counts = new Map<string, number>()
    for (const challenge of props.challenges) {
      const direction = directionLabel(challenge.direction) || translate("ui.uncategorized")
      if (!counts.has(direction)) counts.set(direction, 0)
    }
    for (const solve of props.series?.solves ?? []) {
      const challenge = props.challenges.find(
        (c) => normalizeChallengeKey(c.competitionChallengeId) === normalizeChallengeKey(solve.competitionChallengeId),
      )
      const direction = directionLabel(challenge?.direction) || translate("ui.uncategorized")
      counts.set(direction, (counts.get(direction) ?? 0) + 1)
    }
    const indicators = [...counts.entries()].map(([name, value]) => ({
      name,
      max: Math.max(2, ...counts.values()),
    }))
    return {
      radar: { indicator: indicators, radius: '62%', splitNumber: 3 },
      series: [
        {
          type: 'radar',
          data: [{ name: translate("ui.numberOfProblemsSolved"), value: indicators.map((i) => counts.get(i.name) ?? 0) }],
          areaStyle: { opacity: 0.35 },
        },
      ],
    }
  })

  const challengePieOption = computed<echarts.EChartsCoreOption>(() => {
    const totals = new Map<string, number>()
    for (const solve of props.series?.solves ?? []) {
      const title = challengeTitle.value(solve.competitionChallengeId)
      totals.set(title, (totals.get(title) ?? 0) + (solve.points ?? 0))
    }
    return {
      tooltip: { trigger: 'item', valueFormatter: (v: number | string) => `${v} pts` },
      legend: { type: 'scroll', orient: 'vertical', right: 0, top: 'middle' },
      series: [
        {
          type: 'pie',
          radius: ['30%', '65%'],
          center: ['38%', '50%'],
          label: { show: false },
          data: [...totals.entries()].map(([name, value]) => ({ name, value })),
        },
      ],
    }
  })

  const memberPieOption = computed<echarts.EChartsCoreOption>(() => {
    const totals = new Map<string, number>()
    for (const solve of props.series?.solves ?? []) {
      const name = solve.submitterName || translate("ui.unknownMember")
      totals.set(name, (totals.get(name) ?? 0) + (solve.points ?? 0))
    }
    return {
      tooltip: { trigger: 'item', valueFormatter: (v: number | string) => `${v} pts` },
      series: [
        {
          type: 'pie',
          radius: '65%',
          data: [...totals.entries()].map(([name, value]) => ({ name, value })),
        },
      ],
    }
  })

  const hasCharts = computed(() => (props.series?.solves?.length ?? 0) > 0)

  const ScoreTrendChart = markRaw(ScoreTrendChartComponent)

  return {
      ...toRefs(props),
      Award,
      Medal,
      ShieldCheck,
      Swords,
      Target,
      Users,
      medalRankClass,
      normalizeChallengeKey,
      open,
      isAwdp,
      team,
      challengeTitle,
      solves,
      radarOption,
      challengePieOption,
      memberPieOption,
      hasCharts,
      ScoreTrendChart
    }
}

export type TeamDetailDialogViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useTeamDetailDialog>>>
