<script setup lang="ts">
import { Flag, ShieldCheck, Trophy } from '@lucide/vue'
import type {
  NoCtfapiEndpointsCompetitionsGameModeProtocol,
  NoCtfapiEndpointsCompetitionsScoreboardTeamResponse,
} from '~/api'
import type { echarts } from '~/utils/echarts'
import {
  scoreboardRankingStateLabel,
  scoreboardTeamChallengeScore,
  scoreboardTeamChallengeSignals,
} from '~/utils/scoreboard'
import type { ScoreboardChallengeColumnGroup } from '~/utils/scoreboard'

const props = defineProps<{
  mode?: NoCtfapiEndpointsCompetitionsGameModeProtocol | null
  team: NoCtfapiEndpointsCompetitionsScoreboardTeamResponse | null
  teams: NoCtfapiEndpointsCompetitionsScoreboardTeamResponse[]
  columnGroups: ScoreboardChallengeColumnGroup[]
}>()

const open = defineModel<boolean>('open', { default: false })
const isAwdp = computed(() => props.mode === 'Awdp')

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
    score: props.team ? scoreboardTeamChallengeScore(props.team, group) : 0,
    attackScore: split.attack,
    defenseScore: split.defense,
    signals: props.team
      ? scoreboardTeamChallengeSignals(props.team, group, props.mode)
      : null,
  }
}))

const radarOption = computed<echarts.EChartsCoreOption>(() => {
  const indicators = props.columnGroups.map(group => ({
    name: group.challenge?.title ?? translate('未知题目'),
    max: Math.max(1, ...props.teams.map((team) => {
      if (isAwdp.value) {
        const split = challengeSplitScore(team, group.competitionChallengeId)
        return Math.max(0, split.attack, split.defense)
      }
      return Math.max(0, scoreboardTeamChallengeScore(team, group))
    })),
  }))
  const data = isAwdp.value
    ? [
        { name: translate('攻击分'), value: rows.value.map(row => Math.max(0, row.attackScore)) },
        { name: translate('防御分'), value: rows.value.map(row => Math.max(0, row.defenseScore)) },
      ]
    : [{ name: translate('已结算得分'), value: rows.value.map(row => Math.max(0, row.score)) }]

  return {
    tooltip: { trigger: 'item' },
    legend: { show: isAwdp.value, bottom: 0 },
    radar: {
      indicator: indicators,
      radius: '62%',
      splitNumber: 4,
      axisName: { fontSize: 11 },
    },
    series: [{ type: 'radar', data, areaStyle: { opacity: 0.18 } }],
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
    <DialogScrollContent class="max-h-[90vh] sm:max-w-4xl">
      <DialogHeader>
        <DialogTitle>{{ team?.teamName ?? $t('队伍详情') }}</DialogTitle>
        <DialogDescription>{{ $t('各轴展示题目已结算分值，点击矩阵状态图标可查看逐轮明细。') }}</DialogDescription>
      </DialogHeader>

      <template v-if="team">
        <div class="grid gap-px overflow-hidden rounded-lg border bg-border sm:grid-cols-3">
          <div class="bg-background p-4"><p class="text-xs text-muted-foreground">{{ $t('排名') }}</p><p class="mt-1 font-mono text-lg font-semibold tabular-nums">#{{ team.rank ?? '—' }}</p></div>
          <div class="bg-background p-4"><p class="text-xs text-muted-foreground">{{ $t('总分') }}</p><p class="mt-1 font-mono text-lg font-semibold tabular-nums">{{ team.totalScore ?? 0 }} pts</p></div>
          <div class="bg-background p-4"><p class="text-xs text-muted-foreground">{{ $t('排名状态') }}</p><p class="mt-1 flex items-center gap-2 font-medium"><Trophy class="size-4 text-primary" aria-hidden="true" />{{ scoreboardRankingStateLabel(team.rankingState) }}</p></div>
        </div>

        <section aria-labelledby="scoreboard-team-radar-title">
          <h3 id="scoreboard-team-radar-title" class="mb-2 text-sm font-semibold">{{ $t('题目得分雷达') }}</h3>
          <MiniChart v-if="rows.length" :option="radarOption" height="340px" />
          <p v-else class="py-10 text-center text-sm text-muted-foreground">{{ $t('暂无数据') }}</p>
        </section>

        <div class="overflow-x-auto rounded-lg border">
          <Table>
            <TableHeader><TableRow><TableHead>{{ $t('题目') }}</TableHead><TableHead>{{ $t('状态') }}</TableHead><TableHead v-if="isAwdp" class="text-right">{{ $t('攻击分') }}</TableHead><TableHead v-if="isAwdp" class="text-right">{{ $t('防御分') }}</TableHead><TableHead class="text-right">{{ $t('已结算得分') }}</TableHead></TableRow></TableHeader>
            <TableBody>
              <TableRow v-for="row in rows" :key="row.group.competitionChallengeId">
                <TableCell class="font-medium">{{ row.title }}</TableCell>
                <TableCell>
                  <div v-if="row.signals" class="flex flex-wrap items-center gap-3 text-xs">
                    <span v-if="row.signals.showFlag" class="inline-flex items-center gap-1.5"><Flag class="size-4" :class="row.signals.flagSucceeded ? 'text-emerald-600' : 'text-muted-foreground/50'" aria-hidden="true" />{{ flagLabel(row.signals.flagSucceeded) }}</span>
                    <span v-if="row.signals.showShield" class="inline-flex items-center gap-1.5"><ShieldCheck class="size-4" :class="row.signals.shieldSucceeded ? 'text-emerald-600' : 'text-muted-foreground/50'" aria-hidden="true" />{{ row.signals.shieldSucceeded ? $t('防御成功') : $t('防御未成功') }}</span>
                  </div>
                </TableCell>
                <TableCell v-if="isAwdp" class="text-right font-mono tabular-nums">{{ row.attackScore }} pts</TableCell>
                <TableCell v-if="isAwdp" class="text-right font-mono tabular-nums">{{ row.defenseScore }} pts</TableCell>
                <TableCell class="text-right font-mono font-semibold tabular-nums">{{ row.score }} pts</TableCell>
              </TableRow>
            </TableBody>
          </Table>
        </div>
      </template>
    </DialogScrollContent>
  </Dialog>
</template>
