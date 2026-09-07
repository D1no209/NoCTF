<script setup lang="ts">
import { ChartSpline, Flag, ShieldCheck, Target, Trophy } from '@lucide/vue'
import type {
  NoCtfapiEndpointsCompetitionsGameModeProtocol,
  NoCtfapiEndpointsCompetitionsScoreboardTeamResponse,
} from '~/api'
import type { echarts } from '~/utils/echarts'
import {
  scoreboardDirectionGroups,
  scoreboardMemberContributionSlices,
  scoreboardRankingStateLabel,
  scoreboardTeamChallengeScore,
  scoreboardTeamChallengeSignals,
  scoreboardTeamAchievements,
  scoreboardTeamDirectionScore,
} from '~/utils/scoreboard'
import type { ScoreboardChallengeColumnGroup } from '~/utils/scoreboard'
import type { TrendSeries } from './types'

const props = defineProps<{
  mode?: NoCtfapiEndpointsCompetitionsGameModeProtocol | null
  team: NoCtfapiEndpointsCompetitionsScoreboardTeamResponse | null
  teams: NoCtfapiEndpointsCompetitionsScoreboardTeamResponse[]
  columnGroups: ScoreboardChallengeColumnGroup[]
  trendSeries?: TrendSeries[]
  trendLoading?: boolean
  trendError?: string | null
  trendRangeStart?: string | null
  trendRangeEnd?: string | null
}>()

const emit = defineEmits<{ retryTrends: [] }>()

const open = defineModel<boolean>('open', { default: false })
const isAwdp = computed(() => props.mode === 'Awdp')
const usesCurrentScore = computed(() => props.mode === 'Ctf' || props.mode === 'Koh')
const scoreLabel = computed(() => translate(
  usesCurrentScore.value ? '当前得分' : '已结算得分',
))

const memberContributionSlices = computed(() => scoreboardMemberContributionSlices(props.team))

const memberContributionTotal = computed(() => memberContributionSlices.value
  .reduce((total, contribution) => total + contribution.value, 0))

function memberContributionPercent(value: number): number {
  return memberContributionTotal.value > 0
    ? Math.round(value / memberContributionTotal.value * 1000) / 10
    : 0
}

const memberContributionPieOption = computed<echarts.EChartsCoreOption>(() => ({
  tooltip: {
    trigger: 'item',
    formatter: '{b}<br/>{c} pts · {d}%',
  },
  legend: {
    type: 'scroll',
    bottom: 0,
    left: 'center',
  },
  series: [{
    type: 'pie',
    radius: ['38%', '68%'],
    center: ['50%', '43%'],
    avoidLabelOverlap: true,
    minAngle: 4,
    label: {
      formatter: '{b}\n{c} pts · {d}%',
      fontSize: 11,
      lineHeight: 16,
    },
    labelLine: { length: 12, length2: 8 },
    data: memberContributionSlices.value,
  }],
}))

function challengeSplitScore(
  team: NoCtfapiEndpointsCompetitionsScoreboardTeamResponse,
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
    title: group.challenge?.title ?? translate('未知题目'),
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
          name: translate('攻击分'),
          value: selectedScores.map(score => score.attack),
          lineStyle: { width: 3 },
          areaStyle: { opacity: 0.2 },
          symbol: 'circle',
          symbolSize: 6,
        },
        {
          name: translate('防御分'),
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
    return succeeded ? translate('攻击成功') : translate('攻击未成功')
  if (props.mode === 'Koh')
    return succeeded ? translate('已取得控制权') : translate('尚未取得控制权')
  return succeeded ? translate('已解出') : translate('尚未解出')
}
</script>

<template>
  <Dialog v-model:open="open">
    <DialogContent class="flex max-h-[calc(100dvh-2rem)] flex-col overflow-clip p-0 sm:max-w-5xl">
      <DialogHeader class="shrink-0 border-b px-5 py-4 pr-12">
        <DialogTitle>{{ team?.teamName ?? $t('队伍详情') }}</DialogTitle>
        <DialogDescription>{{ $t('各轴按题目方向汇总计入总分的有效分值，点击矩阵状态图标可查看逐轮明细。') }}</DialogDescription>
      </DialogHeader>

      <div v-if="team" class="flex min-h-0 flex-1 flex-col gap-5 overflow-y-auto overscroll-contain p-4 *:shrink-0 sm:p-5" data-testid="team-detail-scroll">
        <div class="grid gap-px overflow-hidden rounded-lg border bg-border sm:grid-cols-3">
          <div class="bg-background p-4"><p class="text-xs text-muted-foreground">{{ $t('排名') }}</p><p class="mt-1 font-mono text-lg font-semibold tabular-nums">#{{ team.rank ?? '—' }}</p></div>
          <div class="bg-background p-4"><p class="text-xs text-muted-foreground">{{ $t('总分') }}</p><p class="mt-1 font-mono text-lg font-semibold tabular-nums">{{ team.totalScore ?? 0 }} pts</p></div>
          <div class="bg-background p-4"><p class="text-xs text-muted-foreground">{{ $t('排名状态') }}</p><p class="mt-1 flex items-center gap-2 font-medium"><Trophy class="size-4 text-primary" aria-hidden="true" />{{ scoreboardRankingStateLabel(team.rankingState) }}</p></div>
        </div>

        <section v-if="mode === 'Ctf'" class="rounded-xl border bg-muted/20 p-4" aria-labelledby="scoreboard-team-trend-title">
          <div class="mb-2 flex items-start gap-2">
            <ChartSpline class="mt-0.5 size-4 shrink-0 text-primary" aria-hidden="true" />
            <div>
              <h3 id="scoreboard-team-trend-title" class="text-sm font-semibold">{{ $t('队伍得分趋势') }}</h3>
              <p class="text-xs text-muted-foreground">{{ $t('查看单支队伍的累计分值变化') }}</p>
            </div>
          </div>
          <Skeleton v-if="trendLoading && !trendSeries?.length" class="h-[280px] w-full" />
          <Alert v-else-if="trendError" variant="destructive">
            <AlertDescription class="flex flex-wrap items-center justify-between gap-3">
              <span>{{ trendError }}</span>
              <Button type="button" size="sm" variant="outline" @click="emit('retryTrends')">{{ $t('重新加载') }}</Button>
            </AlertDescription>
          </Alert>
          <LazyScoreTrendChart
            v-else-if="trendSeries?.length"
            :series="trendSeries"
            :range-start="trendRangeStart"
            :range-end="trendRangeEnd"
            height="280px"
          />
          <p v-else class="py-10 text-center text-sm text-muted-foreground">{{ $t('暂无得分趋势') }}</p>
        </section>

        <div class="grid gap-4 lg:grid-cols-2">
          <section class="rounded-xl border bg-muted/20 p-4" aria-labelledby="scoreboard-team-radar-title">
            <div class="mb-2 flex items-center gap-2">
              <Target class="size-4 text-primary" aria-hidden="true" />
              <h3 id="scoreboard-team-radar-title" class="text-sm font-semibold">{{ $t('题目方向得分雷达') }}</h3>
            </div>
            <MiniChart v-if="directionGroups.length" :option="radarOption" height="360px" />
            <p v-else class="py-10 text-center text-sm text-muted-foreground">{{ $t('暂无数据') }}</p>
          </section>

          <section class="rounded-xl border bg-muted/20 p-4" aria-labelledby="scoreboard-member-contribution-title">
            <div class="mb-2 flex items-center gap-2">
              <Trophy class="size-4 text-primary" aria-hidden="true" />
              <div>
                <h3 id="scoreboard-member-contribution-title" class="text-sm font-semibold">{{ $t('队员得分占比') }}</h3>
                <p class="text-xs text-muted-foreground">{{ $t('按可归属到队员的正向得分统计') }}</p>
              </div>
            </div>
            <MiniChart v-if="memberContributionSlices.length" :option="memberContributionPieOption" height="360px" />
            <p v-else class="py-10 text-center text-sm text-muted-foreground">{{ $t('暂无可归属的队员得分') }}</p>
            <p class="text-xs leading-5 text-muted-foreground">
              {{ $t('自动结算、历史窗口及其他无法归属个人的分值归入“团队/系统”。') }}
            </p>
            <ul class="sr-only">
              <li v-for="slice in memberContributionSlices" :key="`${slice.userId}:${slice.name}`">
                {{ slice.name }}：{{ slice.value }} pts，{{ memberContributionPercent(slice.value) }}%
              </li>
            </ul>
          </section>
        </div>


        <section class="flex min-w-0 flex-col gap-3">
          <h3 class="font-semibold">{{ mode === 'Ctf' || isAwdp ? $t('已解出题目') : $t('题目详情') }}</h3>
          <p v-if="mode === 'Ctf' || isAwdp" class="text-xs text-muted-foreground">{{ $t('仅显示成功解出的题目，记录首次成功的操作者与提交时间。') }}</p>
          <Empty v-if="!rows.length" class="border">
            <EmptyDescription>{{ team.achievements == null && (mode === 'Ctf' || isAwdp) ? $t('此快照尚无解题者记录，请等待排行榜刷新。') : $t('该队伍暂无已解出的题目') }}</EmptyDescription>
          </Empty>
          <div v-else class="min-w-0 rounded-lg border bg-background">
            <Table>
              <TableHeader><TableRow>
                <TableHead class="min-w-44">{{ $t('题目') }}</TableHead>
                <TableHead class="whitespace-nowrap">{{ $t('状态') }}</TableHead>
                <TableHead v-if="mode === 'Ctf' || isAwdp" class="min-w-28">{{ $t('解出人') }}</TableHead>
                <TableHead v-if="mode === 'Ctf' || isAwdp" class="whitespace-nowrap">{{ $t('解题时间') }}</TableHead>
                <TableHead v-if="isAwdp" class="whitespace-nowrap text-right">{{ $t('攻击分') }}</TableHead>
                <TableHead v-if="isAwdp" class="whitespace-nowrap text-right">{{ $t('防御分') }}</TableHead>
                <TableHead class="whitespace-nowrap text-right">{{ scoreLabel }}</TableHead>
              </TableRow></TableHeader>
              <TableBody>
                <template v-for="row in rows" :key="row.group.competitionChallengeId">
                  <TableRow v-for="(achievement, index) in row.achievements.length ? row.achievements : [null]" :key="achievement?.kind ?? 'status'">
                    <TableCell v-if="index === 0" :rowspan="Math.max(1, row.achievements.length)" class="max-w-64 whitespace-normal break-words font-medium">{{ row.title }}</TableCell>
                    <TableCell class="whitespace-nowrap">
                      <span v-if="achievement" class="inline-flex items-center gap-1.5 text-xs">
                        <ShieldCheck v-if="achievement.kind === 'Defense'" class="size-4 text-primary" aria-hidden="true" /><Flag v-else class="size-4 text-primary" aria-hidden="true" />
                        {{ achievement.kind === 'Defense' ? $t('防御成功') : flagLabel(true) }}
                      </span>
                      <div v-else-if="row.signals" class="flex flex-wrap items-center gap-3 text-xs">
                        <span v-if="row.signals.showFlag">{{ flagLabel(row.signals.flagSucceeded) }}</span>
                        <span v-if="row.signals.showShield">{{ row.signals.shieldSucceeded ? $t('防御成功') : $t('防御未成功') }}</span>
                      </div>
                    </TableCell>
                    <TableCell v-if="mode === 'Ctf' || isAwdp" class="max-w-48 whitespace-normal break-words">{{ achievement?.displayName || $t('未记录解题者') }}</TableCell>
                    <TableCell v-if="mode === 'Ctf' || isAwdp" class="whitespace-nowrap font-mono text-xs tabular-nums">{{ achievement?.occurredAt ? formatDateTime(achievement.occurredAt) : '—' }}</TableCell>
                    <TableCell v-if="isAwdp && index === 0" :rowspan="Math.max(1, row.achievements.length)" class="whitespace-nowrap text-right font-mono tabular-nums">{{ row.attackScore }} pts</TableCell>
                    <TableCell v-if="isAwdp && index === 0" :rowspan="Math.max(1, row.achievements.length)" class="whitespace-nowrap text-right font-mono tabular-nums">{{ row.defenseScore }} pts</TableCell>
                    <TableCell v-if="index === 0" :rowspan="Math.max(1, row.achievements.length)" class="whitespace-nowrap text-right font-mono font-semibold tabular-nums">{{ row.score }} pts</TableCell>
                  </TableRow>
                </template>
              </TableBody>
            </Table>
          </div>
        </section>
      </div>
    </DialogContent>
  </Dialog>
</template>
