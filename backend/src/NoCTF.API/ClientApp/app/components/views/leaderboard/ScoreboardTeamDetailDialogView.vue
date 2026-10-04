<script setup lang="ts">
import { toRefs } from 'vue'
import type { ScoreboardTeamDetailDialogViewState } from '~/features/leaderboard/useScoreboardTeamDetailDialog'

const viewProps = defineProps<{ state: ScoreboardTeamDetailDialogViewState }>()
const { ChartSpline, Flag, ShieldCheck, Target, Trophy, scoreboardRankingStateLabel, emit, open, isAwdp, scoreLabel, memberContributionSlices, memberContributionRows, memberContributionPieOption, rows, directionGroups, radarOption, flagLabel, mode, team, trendSeries, trendLoading, trendError, trendRangeStart, trendRangeEnd, trendRevision, LazyScoreTrendChart } = toRefs(viewProps.state)
</script>

<template>
  <Dialog v-model:open="open">
    <DialogContent class="flex max-h-[calc(100dvh-2rem)] flex-col overflow-clip p-0 sm:max-w-5xl">
      <DialogHeader class="shrink-0 border-b px-5 py-4 pr-12">
        <DialogTitle>{{ team?.teamName ?? $t('common.label.teamDetails') }}</DialogTitle>
        <DialogDescription>{{ $t('leaderboard.scoreboardTeam.description.axisAggregatesEffectiveScores') }}</DialogDescription>
      </DialogHeader>

      <ScrollSurface as="div" v-if="team" class="flex min-h-0 flex-1 flex-col gap-5 overflow-y-auto overscroll-contain p-4 *:shrink-0 sm:p-5" data-testid="team-detail-scroll">
        <div class="grid gap-px overflow-hidden rounded-lg border bg-border sm:grid-cols-3">
          <div class="bg-background p-4"><p class="text-xs text-muted-foreground">{{ $t('common.label.ranking') }}</p><p class="mt-1 font-mono text-lg font-semibold tabular-nums">#{{ team.rank ?? $t('common.label.symbol') }}</p></div>
          <div class="bg-background p-4"><p class="text-xs text-muted-foreground">{{ $t('common.label.totalScore') }}</p><p class="mt-1 font-mono text-lg font-semibold tabular-nums">{{ team.totalScore ?? 0 }} {{ $t('common.label.pts.scoreTrendChart') }}</p></div>
          <div class="bg-background p-4"><p class="text-xs text-muted-foreground">{{ $t('leaderboard.label.rankingStatus') }}</p><p class="mt-1 flex items-center gap-2 font-medium"><Trophy class="size-4 text-primary" aria-hidden="true" />{{ scoreboardRankingStateLabel(team.rankingState) }}</p></div>
        </div>

        <section v-if="mode === 'Ctf'" class="rounded-xl border bg-muted/20 p-4" aria-labelledby="scoreboard-team-trend-title">
          <div class="mb-2 flex items-start gap-2">
            <ChartSpline class="mt-0.5 size-4 shrink-0 text-primary" aria-hidden="true" />
            <div>
              <h3 id="scoreboard-team-trend-title" class="text-sm font-semibold">{{ $t('leaderboard.label.teamScoreTrend') }}</h3>
              <p class="text-xs text-muted-foreground">{{ $t('leaderboard.scoreboardTeam.description.viewCumulativeScoreChanges') }}</p>
            </div>
          </div>
          <Skeleton v-if="trendLoading && !trendSeries?.length" class="h-[280px] w-full" />
          <Alert v-else-if="trendError" variant="destructive">
            <AlertDescription class="flex flex-wrap items-center justify-between gap-3">
              <span>{{ $message(trendError) }}</span>
              <Button type="button" size="sm" variant="outline" @click="emit('retryTrends')">{{ $t('common.label.reload') }}</Button>
            </AlertDescription>
          </Alert>
          <component :is="LazyScoreTrendChart"
            v-else-if="trendSeries?.length"
            :series="trendSeries"
            :revision="`${trendRevision ?? 'none'}:${team.teamId}`"
            :range-start="trendRangeStart"
            :range-end="trendRangeEnd"
            height="280px"
          />
          <p v-else class="py-10 text-center text-sm text-muted-foreground">{{ $t('leaderboard.scoreboardTeam.label.scoreTrendDataYet') }}</p>
        </section>

        <div class="grid gap-4 lg:grid-cols-2">
          <section class="rounded-xl border bg-muted/20 p-4" aria-labelledby="scoreboard-team-radar-title">
            <div class="mb-2 flex items-center gap-2">
              <Target class="size-4 text-primary" aria-hidden="true" />
              <h3 id="scoreboard-team-radar-title" class="text-sm font-semibold">{{ $t('leaderboard.label.scoreCategory') }}</h3>
            </div>
            <MiniChart v-if="directionGroups.length" :option="radarOption" height="360px" />
            <p v-else class="py-10 text-center text-sm text-muted-foreground">{{ $t('leaderboard.label.dataYet') }}</p>
          </section>

          <section class="rounded-xl border bg-muted/20 p-4" aria-labelledby="scoreboard-member-contribution-title">
            <div class="mb-2 flex items-center gap-2">
              <Trophy class="size-4 text-primary" aria-hidden="true" />
              <div>
                <h3 id="scoreboard-member-contribution-title" class="text-sm font-semibold">{{ $t('leaderboard.label.memberScoreContribution') }}</h3>
                <p class="text-xs text-muted-foreground">{{ $t('leaderboard.scoreboardTeam.description.basedPositivePointsAttributable') }}</p>
              </div>
            </div>
            <MiniChart v-if="memberContributionSlices.length" :option="memberContributionPieOption" height="260px" />
            <p v-else class="py-10 text-center text-sm text-muted-foreground">{{ $t('leaderboard.scoreboardTeam.label.memberAttributedScoreYet') }}</p>
            <ul v-if="memberContributionRows.length" class="mt-2 space-y-1.5">
              <li v-for="row in memberContributionRows" :key="`${row.userId}:${row.name}`" class="flex items-start gap-3 text-sm">
                <span class="mt-1 size-2.5 shrink-0 rounded-full" :style="{ backgroundColor: row.color }" aria-hidden="true" />
                <span class="min-w-0 flex-1 break-words font-medium text-foreground">{{ row.name }}</span>
                <span class="shrink-0 whitespace-nowrap font-mono font-semibold tabular-nums text-foreground">
                  {{ row.value }} {{ $t('leaderboard.label.pts') }} · {{ row.percent }}%
                </span>
              </li>
            </ul>
            <p class="text-xs leading-5 text-muted-foreground">
              {{ $t('leaderboard.scoreboardTeam.description.automatedSettlementHistoricalWindows') }}
            </p>
          </section>
        </div>


        <section class="flex min-w-0 flex-col gap-3">
          <h3 class="font-semibold">{{ mode === 'Ctf' || isAwdp ? $t('leaderboard.label.solvedChallenges') : $t('leaderboard.label.challengeDetails') }}</h3>
          <p v-if="mode === 'Ctf' || isAwdp" class="text-xs text-muted-foreground">{{ $t('leaderboard.scoreboardTeam.description.successfulChallengesShownFirst') }}</p>
          <Empty v-if="!rows.length" class="border">
            <EmptyDescription>{{ team.achievements == null && (mode === 'Ctf' || isAwdp) ? $t('leaderboard.scoreboardTeam.description.snapshotSolverAttributionYet') : $t('leaderboard.scoreboardTeam.description.teamSolvedChallengesYet') }}</EmptyDescription>
          </Empty>
          <div v-else class="min-w-0 rounded-lg border bg-background">
            <Table>
              <TableHeader><TableRow>
                <TableHead class="min-w-44">{{ $t('common.label.challenge.pageTitle') }}</TableHead>
                <TableHead class="whitespace-nowrap">{{ $t('common.label.status') }}</TableHead>
                <TableHead v-if="mode === 'Ctf' || isAwdp" class="min-w-28">{{ $t('leaderboard.label.solved') }}</TableHead>
                <TableHead v-if="mode === 'Ctf' || isAwdp" class="whitespace-nowrap">{{ $t('leaderboard.label.solved.detailDialogView') }}</TableHead>
                <TableHead v-if="isAwdp" class="whitespace-nowrap text-right">{{ $t('common.label.attackScore') }}</TableHead>
                <TableHead v-if="isAwdp" class="whitespace-nowrap text-right">{{ $t('common.label.defenseScore') }}</TableHead>
                <TableHead class="whitespace-nowrap text-right">{{ scoreLabel }}</TableHead>
              </TableRow></TableHeader>
              <TableBody>
                <template v-for="row in rows" :key="row.group.competitionChallengeId ?? undefined">
                  <TableRow v-for="(achievement, index) in row.achievements.length ? row.achievements : [null]" :key="achievement?.kind ?? 'status'">
                    <TableCell v-if="index === 0" :rowspan="Math.max(1, row.achievements.length)" class="max-w-64 whitespace-normal break-words font-medium">{{ row.title }}</TableCell>
                    <TableCell class="whitespace-nowrap">
                      <span v-if="achievement" class="inline-flex items-center gap-1.5 text-xs">
                        <ShieldCheck v-if="achievement.kind === 'Defense'" class="size-4 text-primary" aria-hidden="true" /><Flag v-else class="size-4 text-primary" aria-hidden="true" />
                        {{ achievement.kind === 'Defense' ? $t('common.label.defenseSucceeded') : flagLabel(true) }}
                      </span>
                      <div v-else-if="row.signals" class="flex flex-wrap items-center gap-3 text-xs">
                        <span v-if="row.signals.showFlag">{{ flagLabel(row.signals.flagSucceeded) }}</span>
                        <span v-if="row.signals.showShield">{{ row.signals.shieldSucceeded ? $t('common.label.defenseSucceeded') : $t('leaderboard.label.successfulDefense') }}</span>
                      </div>
                    </TableCell>
                    <TableCell v-if="mode === 'Ctf' || isAwdp" class="max-w-48 whitespace-normal break-words">{{ achievement?.displayName || $t('leaderboard.label.solverRecorded') }}</TableCell>
                    <TableCell v-if="mode === 'Ctf' || isAwdp" class="whitespace-nowrap font-mono text-xs tabular-nums">{{ achievement?.occurredAt ? formatDateTime(achievement.occurredAt) : $t('common.label.symbol') }}</TableCell>
                    <TableCell v-if="isAwdp && index === 0" :rowspan="Math.max(1, row.achievements.length)" class="whitespace-nowrap text-right font-mono tabular-nums">{{ row.attackScore }} {{ $t('common.label.pts.scoreTrendChart') }}</TableCell>
                    <TableCell v-if="isAwdp && index === 0" :rowspan="Math.max(1, row.achievements.length)" class="whitespace-nowrap text-right font-mono tabular-nums">{{ row.defenseScore }} {{ $t('common.label.pts.scoreTrendChart') }}</TableCell>
                    <TableCell v-if="index === 0" :rowspan="Math.max(1, row.achievements.length)" class="whitespace-nowrap text-right font-mono font-semibold tabular-nums">{{ row.score }} {{ $t('common.label.pts.scoreTrendChart') }}</TableCell>
                  </TableRow>
                </template>
              </TableBody>
            </Table>
          </div>
        </section>
      </ScrollSurface>
    </DialogContent>
  </Dialog>
</template>
