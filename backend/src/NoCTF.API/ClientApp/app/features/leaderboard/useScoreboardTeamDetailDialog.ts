import type { UiMessage } from '../../utils/i18n'
import { defineAsyncComponent } from 'vue'
import { markRaw, toRefs } from 'vue'
import type { Ref } from 'vue'
import { ChartSpline, Flag, ShieldCheck, Target, Trophy } from '@lucide/vue'
import type { NoCTFAPIEndpointsCompetitionsGameModeProtocol, NoCTFAPIEndpointsCompetitionsScoreboardTeamResponse } from '../../api/models'
import type { echarts } from '../../utils/echarts'
import { scoreboardDirectionGroups, scoreboardMemberContributionSlices, scoreboardRankingStateLabel, scoreboardTeamChallengeScore, scoreboardTeamChallengeSignals, scoreboardTeamAchievements, scoreboardTeamDirectionScore } from '../../utils/scoreboard'
import type { ScoreboardChallengeColumnGroup } from '../../utils/scoreboard'
import type { TrendSeries } from './types'

/** Owns state, effects and commands for ScoreboardTeamDetailDialog. */
export function useScoreboardTeamDetailDialog(props: Readonly<{
  mode?: NoCTFAPIEndpointsCompetitionsGameModeProtocol | null
  team: NoCTFAPIEndpointsCompetitionsScoreboardTeamResponse | null
  teams: NoCTFAPIEndpointsCompetitionsScoreboardTeamResponse[]
  columnGroups: ScoreboardChallengeColumnGroup[]
  trendSeries?: TrendSeries[]
  trendLoading?: boolean
  trendError?: UiMessage | null
  trendRangeStart?: string | null
  trendRangeEnd?: string | null
  trendRevision?: string | number | null
}>,
emit: { (event: "retryTrends", ...args: []): void },
open: Ref<boolean>) {
  const isAwdp = computed(() => props.mode === 'Awdp')

  const usesCurrentScore = computed(() => props.mode === 'Ctf' || props.mode === 'Koh')

  const scoreLabel = computed(() => translate(
    usesCurrentScore.value ? translate("leaderboard.label.score") : translate("leaderboard.label.settledScore"),
  ))

  const memberContributionSlices = computed(() => scoreboardMemberContributionSlices(props.team))

  const memberContributionTotal = computed(() => memberContributionSlices.value
    .reduce((total, contribution) => total + contribution.value, 0))

  function memberContributionPercent(value: number): number {
    return memberContributionTotal.value > 0
      ? Math.round(value / memberContributionTotal.value * 1000) / 10
      : 0
  }

  const memberContributionRows = computed(() => memberContributionSlices.value.map((slice, index) => ({
    ...slice,
    percent: memberContributionPercent(slice.value),
    color: `var(--chart-${index % 10 + 1})`,
  })))

  const memberContributionPieOption = computed<echarts.EChartsCoreOption>(() => ({
    tooltip: {
      trigger: 'item',
      formatter: '{b}<br/>{c} pts · {d}%',
    },
    legend: { show: false },
    series: [{
      type: 'pie',
      radius: ['38%', '68%'],
      center: ['50%', '50%'],
      minAngle: 4,
      label: { show: false },
      labelLine: { show: false },
      data: memberContributionSlices.value,
    }],
  }))

  function challengeSplitScore(
    team: NoCTFAPIEndpointsCompetitionsScoreboardTeamResponse,
    competitionChallengeId: string,
  ): { attack: number; defense: number } {
    const score = (team.challengeScores ?? []).find(
      item => item.competitionChallengeId === competitionChallengeId,
    )
    return { attack: score?.attackScore ?? 0, defense: score?.defenseScore ?? 0 }
  }

  const rows = computed(() => props.columnGroups.map((group) => {
    const split = props.team
      ? challengeSplitScore(props.team, group.competitionChallengeId)
      : { attack: 0, defense: 0 }
    return {
      group,
      title: group.challenge?.title ?? translate("common.label.unknownQuestion"),
      score: props.team ? scoreboardTeamChallengeScore(props.team, group, props.mode) : 0,
      attackScore: split.attack,
      defenseScore: split.defense,
      signals: props.team
        ? scoreboardTeamChallengeSignals(props.team, group, props.mode)
        : null,
      achievements: scoreboardTeamAchievements(props.team, group.competitionChallengeId, props.mode),
    }
  }).filter(row => props.mode !== 'Ctf' && props.mode !== 'Awdp' || row.achievements.length > 0))

  const directionGroups = computed(() => scoreboardDirectionGroups(props.columnGroups))

  const radarOption = computed<echarts.EChartsCoreOption>(() => {
    const indicators = directionGroups.value.map((direction) => {
      const maximum = Math.max(1, ...props.teams.flatMap((team) => {
        const score = scoreboardTeamDirectionScore(team, direction, props.mode)
        return isAwdp.value ? [score.attack, score.defense] : [score.total]
      }))
      return {
        name: direction.name,
        max: Math.max(1, Math.ceil(maximum * 1.1)),
      }
    })
    const selectedScores = props.team
      ? directionGroups.value.map(direction => scoreboardTeamDirectionScore(props.team!, direction, props.mode))
      : []
    const data = isAwdp.value
      ? [
          {
            name: translate("common.label.attackScore"),
            value: selectedScores.map(score => score.attack),
            lineStyle: { width: 3 },
            areaStyle: { opacity: 0.2 },
            symbol: 'circle',
            symbolSize: 6,
          },
          {
            name: translate("common.label.defenseScore"),
            value: selectedScores.map(score => score.defense),
            lineStyle: { width: 3 },
            areaStyle: { opacity: 0.2 },
            symbol: 'circle',
            symbolSize: 6,
          },
        ]
      : [{
          name: scoreLabel.value,
          value: selectedScores.map(score => score.total),
          lineStyle: { width: 3 },
          areaStyle: { opacity: 0.35 },
          symbol: 'circle',
          symbolSize: 6,
        }]

    return {
      tooltip: { trigger: 'item' },
      legend: { show: isAwdp.value, bottom: 2 },
      radar: {
        indicator: indicators,
        center: ['50%', '48%'],
        radius: '62%',
        splitNumber: 3,
        axisName: { fontSize: 12 },
        splitArea: { areaStyle: { color: 'transparent' } },
        splitLine: { lineStyle: { color: 'rgba(148,163,184,0.28)' } },
        axisLine: { lineStyle: { color: 'rgba(148,163,184,0.34)' } },
      },
      series: [{ type: 'radar', data }],
    }
  })

  function flagLabel(succeeded: boolean): string {
    if (props.mode === 'Awd' || props.mode === 'Awdp')
      return succeeded ? translate("common.label.attackSucceeded") : translate("leaderboard.label.successfulAttack")
    if (props.mode === 'Koh')
      return succeeded ? translate("leaderboard.label.controlAcquired") : translate("leaderboard.label.controlAcquired.scoreboardSlotStatus")
    return succeeded ? translate("common.label.solved.competitionChallengeNavigator") : translate("common.label.solved")
  }

  const LazyScoreTrendChart = markRaw(defineAsyncComponent(() => import('./ScoreTrendChart.vue')))

  return {
      LazyScoreTrendChart,
      ...toRefs(props),
      ChartSpline,
      Flag,
      ShieldCheck,
      Target,
      Trophy,
      scoreboardRankingStateLabel,
      emit,
      open,
      isAwdp,
      scoreLabel,
      memberContributionSlices,
      memberContributionPercent,
      memberContributionRows,
      memberContributionPieOption,
      rows,
      directionGroups,
      radarOption,
      flagLabel
    }
}

export type ScoreboardTeamDetailDialogViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useScoreboardTeamDetailDialog>>>
