<script setup lang="ts">
import { toRefs } from 'vue'
import type { CompetitionsByIdLeaderboardPageViewState } from '~/features/routes/competitions/[id]/useCompetitionsByIdLeaderboardPage'

const viewProps = defineProps<{ state: CompetitionsByIdLeaderboardPageViewState }>()
const { ArrowLeft, ChevronLeft, ChevronRight, Download, History, Medal, Trophy, medalBloodRankClass, medalRankClass, scoreboardBloodAward, scoreboardEntryKindLabel, scoreboardEntryOutcomeLabel, scoreboardRankingStateLabel, scoreboardSlot, competitionId, competitionReturnPath, dampenLeaderboardWheel, board, allTracksKey, selectedTrackKey, tracksEnabled, canViewInternalTracks, showLeaderboardHiddenTeams, availableTracks, selectedAllTracks, trackName, teams, displayRank, visibleTeams, showMoreTeams, displayTeamName, columnGroups, isCtf, trends, trendsLoading, trendsError, visibleTrendSeries, trendRangeStart, trendRangeEnd, trendRevision, loadTrends, roundWindowLabel, roundLabel, slotTitle, ctfScore, exportCsv, detailOpen, teamDetailOpen, teamDetailTeam, teamDetailTrendSeries, detailLoading, detailLoadingMore, detailError, detail, detailEntries, detailTeam, detailColumn, loadDetailPage, openDetail, openTeamDetail, showOlderRoundWindow, showNewerRoundWindow, showLatestRoundWindow, entryActor, adjustmentOpen, adjustmentLoading, adjustmentLoadingMore, adjustmentError, adjustmentDetail, adjustmentEntries, adjustmentTeam, loadAdjustmentPage, openAdjustments, adjustmentActor, adjustmentKind, ScoreboardSlotStatus, LazyScoreTrendChart, LazyScoreboardTeamDetailDialog } = toRefs(viewProps.state)
</script>

<template>
  <ScrollSurface as="div" axis="y" data-scoreboard-page-scroll class="min-h-0 flex-1 overflow-y-auto overscroll-contain pr-3" @wheel.capture="dampenLeaderboardWheel">
    <div class="flex flex-col gap-6 pb-4">
    <div>
      <Button variant="ghost" size="sm" as-child>
        <NuxtLink :to="competitionReturnPath">
          <ArrowLeft data-icon="inline-start" />{{ $t('ui.backToCompetition') }}
        </NuxtLink>
      </Button>
    </div>
    <Alert v-if="board.error.value" variant="destructive">
      <AlertDescription class="flex items-center justify-between gap-3"><span>{{ $message(board.error.value) }}</span><Button variant="outline" size="sm" @click="board.refresh()">{{ $t('ui.retry') }}</Button></AlertDescription>
    </Alert>
    <div v-if="board.loading.value && !board.snapshot.value" class="flex flex-col gap-4">
      <Alert><AlertDescription class="flex items-center gap-2"><Spinner class="size-3" />{{ $t('ui.scoreboardDataIsBeingProjectedPleaseWait') }}</AlertDescription></Alert>
      <Skeleton class="h-64 w-full" />
    </div>
    <template v-else-if="board.snapshot.value && board.schema.value && board.catalog.value">
      <div class="flex flex-wrap items-center justify-between gap-3">
        <h2 class="flex items-center gap-2 text-display text-xl"><Trophy class="size-5 text-primary" />{{ $t('ui.leaderboard2') }}<Badge v-if="board.refreshing.value" variant="secondary">{{ $t('ui.refreshing') }}</Badge></h2>
        <div class="flex flex-wrap items-center gap-3">
          <div v-if="board.schema.value.mode === 'Awdp' || board.schema.value.mode === 'Awd'" class="flex flex-wrap items-center gap-2">
            <Badge variant="outline" class="font-mono tabular-nums">{{ roundWindowLabel }}</Badge>
            <Button variant="outline" size="sm" :disabled="board.refreshing.value || !board.canShowOlderRounds.value" @click="showOlderRoundWindow">
              <ChevronLeft data-icon="inline-start" />{{ $t('ui.earlierRounds') }}
            </Button>
            <Button variant="outline" size="sm" :disabled="board.refreshing.value || !board.canShowNewerRounds.value" @click="showNewerRoundWindow">
              {{ $t('ui.laterRounds') }}<ChevronRight data-icon="inline-end" />
            </Button>
            <Button v-if="!board.viewingLatestRounds.value" variant="outline" size="sm" :disabled="board.refreshing.value" @click="showLatestRoundWindow">
              <History data-icon="inline-start" />{{ $t('ui.backToLatestRounds') }}
            </Button>
          </div>
          <div v-if="canViewInternalTracks && tracksEnabled" class="flex items-center gap-2">
            <Label :for="`leaderboard-hidden-teams-${competitionId}`" class="cursor-pointer text-sm font-medium">{{ $t('ui.showLeaderboardHiddenTeams') }}</Label>
            <Switch :id="`leaderboard-hidden-teams-${competitionId}`" v-model="showLeaderboardHiddenTeams" />
          </div>
          <Select v-if="tracksEnabled && availableTracks.length > 1" v-model="selectedTrackKey">
            <SelectTrigger class="min-w-40" :aria-label="$t('ui.selectLeaderboardTrack')"><SelectValue /></SelectTrigger>
            <SelectContent>
              <SelectItem :value="allTracksKey">{{ $t('ui.allTracks') }}</SelectItem>
              <SelectItem v-for="track in availableTracks" :key="track.key" :value="track.key!">{{ track.name }}</SelectItem>
            </SelectContent>
          </Select>
          <Button variant="outline" :disabled="!teams.length" @click="exportCsv"><Download data-icon="inline-start" />{{ $t('ui.downloadAsExcel') }}</Button>
        </div>
      </div>
      <Alert v-if="board.processing.value"><AlertDescription>{{ $t('ui.scoreboardDataIsBeingProjectedPleaseWait') }}</AlertDescription></Alert>
      <Alert v-if="board.snapshot.value.dataScope === 'Frozen'"><AlertDescription>{{ $t('ui.theLeaderboardIsFrozenThisSnapshotIsCurrentAsOf', { time: formatDateTime(board.snapshot.value.dataAsOf) }) }}</AlertDescription></Alert>
      <Empty v-if="board.snapshot.value.dataScope === 'Hidden'" class="border py-12"><EmptyHeader><EmptyTitle>{{ $t('ui.theRankingsAreNotPublicYet') }}</EmptyTitle><EmptyDescription>{{ $t('ui.theOrganizerCurrentlyHidesRankingData') }}</EmptyDescription></EmptyHeader></Empty>
      <template v-else>
        <Alert v-if="isCtf && trendsError" variant="destructive"><AlertDescription class="flex items-center justify-between gap-3"><span>{{ $message(trendsError) }}</span><Button variant="outline" size="sm" @click="loadTrends">{{ $t('ui.retry') }}</Button></AlertDescription></Alert>
        <div v-if="isCtf" data-slot="leaderboard-trend-panel">
          <Card class="overflow-visible">
            <CardContent class="py-4">
              <Skeleton v-if="trendsLoading && !trends" class="h-[320px] w-full" />
              <Empty v-else-if="!visibleTrendSeries.length" class="h-[320px]"><EmptyHeader><EmptyTitle>{{ $t('ui.noScoreTrendDataYet') }}</EmptyTitle></EmptyHeader></Empty>
              <component :is="LazyScoreTrendChart" v-else :series="visibleTrendSeries" :revision="trendRevision" :range-start="trendRangeStart" :range-end="trendRangeEnd" height="clamp(220px, 32vh, 320px)" />
            </CardContent>
          </Card>
        </div>
      <Card>
        <CardContent class="pt-6">
          <Empty v-if="!teams.length" class="border py-8"><EmptyHeader><EmptyTitle>{{ $t('ui.noTeamHasScoredYet') }}</EmptyTitle></EmptyHeader></Empty>
          <div v-else class="min-w-0">
            <Table pin-horizontal-scrollbar class="min-w-max table-auto">
              <TableHeader>
                <TableRow>
                  <TableHead :rowspan="isCtf ? 1 : 2" data-scoreboard-frozen-corner="top-start" class="sticky left-0 z-30 w-20 min-w-20 max-w-20 bg-card text-center">{{ $t('ui.ranking') }}</TableHead>
                  <TableHead :rowspan="isCtf ? 1 : 2" class="sticky left-20 z-30 w-56 min-w-56 max-w-56 bg-card">{{ $t('ui.teams') }}</TableHead>
                  <TableHead :rowspan="isCtf ? 1 : 2" data-scoreboard-frozen-corner="top-end" class="sticky left-76 z-30 w-28 min-w-28 max-w-28 border-r bg-card text-right">{{ $t('ui.totalScore') }}</TableHead>
                  <TableHead v-for="group in columnGroups" :key="group.competitionChallengeId" :colspan="group.columns.length" class="border-l px-4 text-center">
                    <span class="inline-flex items-center gap-1.5 whitespace-nowrap"><LucideIcon v-if="group.challenge?.directionIcon" :name="group.challenge.directionIcon" class="size-4" :class="directionTextClass(group.challenge.direction)" /><component v-else :is="directionIcon(group.challenge?.direction)" class="size-4" :class="directionTextClass(group.challenge?.direction)" />{{ group.challenge?.title ?? $t('ui.unknownQuestion') }}</span>
                  </TableHead>
                </TableRow>
                <TableRow v-if="!isCtf"><template v-for="group in columnGroups" :key="`${group.competitionChallengeId}-rounds`"><TableHead v-for="column in group.columns" :key="column.index" class="min-w-28 border-l px-3 text-center">{{ roundLabel(column) }}</TableHead></template></TableRow>
              </TableHeader>
              <TableBody>
                <TableRow v-for="(team, teamIndex) in visibleTeams" :key="team.teamId" :class="(displayRank(team) ?? 99) <= 3 ? 'bg-primary/5' : ''">
                  <TableCell :data-scoreboard-frozen-corner="teamIndex === visibleTeams.length - 1 ? 'bottom-start' : undefined" class="sticky left-0 z-20 w-20 min-w-20 max-w-20 bg-card text-center"><Medal v-if="(displayRank(team) ?? 99) <= 3" class="size-5" :class="medalRankClass[displayRank(team) ?? 0]" /><span v-else class="font-mono tabular-nums">{{ displayRank(team) ?? $t('ui.symbol') }}</span></TableCell>
                  <TableCell class="sticky left-20 z-20 w-56 min-w-56 max-w-56 bg-card"><div class="flex min-w-0 flex-col items-start gap-1"><Hint :content="displayTeamName(team)" ><ActionButton type="button" class="w-full whitespace-normal break-words rounded-sm text-left font-medium underline-offset-4 hover:text-primary hover:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"  :aria-label="$t('ui.viewDetailsForTeam', { team: displayTeamName(team) })" @click="openTeamDetail(team)">{{ displayTeamName(team) }}</ActionButton></Hint><div v-if="(tracksEnabled && selectedAllTracks && availableTracks.length > 1) || team.rankingState !== 'Eligible'" class="flex flex-wrap items-center gap-1"><Hint v-if="tracksEnabled && selectedAllTracks && availableTracks.length > 1" :content="trackName(team.trackKey)"><Badge variant="outline" class="whitespace-normal break-words text-left">{{ trackName(team.trackKey) }}</Badge></Hint><Badge v-if="team.rankingState !== 'Eligible'" variant="destructive">{{ scoreboardRankingStateLabel(team.rankingState) }}</Badge></div></div></TableCell>
                  <TableCell :data-scoreboard-frozen-corner="teamIndex === visibleTeams.length - 1 ? 'bottom-end' : undefined" class="sticky left-76 z-20 w-28 min-w-28 max-w-28 border-r bg-card text-right">
                    <ActionButton v-if="(team.globalAdjustmentCount ?? 0) > 0" type="button" class="w-full rounded-md px-2 py-1 text-right transition-colors hover:bg-muted focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring" @click="openAdjustments(team)">
                      <span class="block font-mono font-semibold tabular-nums">{{ team.totalScore ?? 0 }} {{ $t('ui.pts2') }}</span>
                      <span class="mt-1 block text-[0.7rem] text-muted-foreground">{{ $t('ui.globalAdjustments2', { count: team.globalAdjustmentCount ?? 0 }) }}</span>
                    </ActionButton>
                    <span v-else class="font-mono font-semibold tabular-nums">{{ team.totalScore ?? 0 }} {{ $t('ui.pts2') }}</span>
                  </TableCell>
                  <template v-for="group in columnGroups" :key="`${team.teamId}-${group.competitionChallengeId}`">
                    <TableCell v-for="column in group.columns" :key="column.index" class="border-l p-1 text-center" :class="isCtf ? 'min-w-56' : 'min-w-28'">
                      <ActionButton v-if="column.index !== undefined && scoreboardSlot(team, column.index)" type="button" class="flex min-h-12 w-full items-center justify-center rounded-md px-1 py-1 transition-colors hover:bg-muted focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring" :aria-label="$t('ui.viewSDetailsFor', { team: displayTeamName(team), challenge: group.challenge?.title ?? $t('ui.unknownQuestion'), round: roundLabel(column) })" @click="openDetail(team, column)">
                        <template v-if="isCtf">
                          <span v-if="ctfScore(scoreboardSlot(team, column.index!)) !== null" class="inline-flex items-center justify-center gap-2 whitespace-nowrap">
                            <span class="font-mono text-lg font-bold tabular-nums">{{ ctfScore(scoreboardSlot(team, column.index!)) }} {{ $t('ui.pts2') }}</span>
                            <Hint :content="`${scoreboardBloodAward(scoreboardSlot(team, column.index!))?.label} +${scoreboardBloodAward(scoreboardSlot(team, column.index!))?.points} pts`" v-if="scoreboardBloodAward(scoreboardSlot(team, column.index!))"><span tabindex="0"

                              class="inline-flex items-center gap-1 text-primary"

                            >
                              <Medal
                                class="size-4 shrink-0"
                                :class="medalBloodRankClass(scoreboardBloodAward(scoreboardSlot(team, column.index!))?.award)"
                                aria-hidden="true"
                              />
                              <span class="sr-only">{{ scoreboardBloodAward(scoreboardSlot(team, column.index!))?.label }}</span>
                              <span class="font-mono text-xs font-semibold tabular-nums">+{{ scoreboardBloodAward(scoreboardSlot(team, column.index!))?.points }} {{ $t('ui.pts2') }}</span>
                            </span></Hint>
                          </span>
                          <span v-else class="font-mono text-sm text-muted-foreground/60">-</span>
                        </template>
                        <component :is="ScoreboardSlotStatus" v-else :mode="board.schema.value.mode" :slot="scoreboardSlot(team, column.index!)!" />
                      </ActionButton>
                      <span v-else class="font-mono text-sm text-muted-foreground/60">-</span>
                    </TableCell>
                  </template>
                </TableRow>
              </TableBody>
            </Table>
          </div>
          <div v-if="visibleTeams.length < teams.length" class="mt-4 flex justify-center"><Button variant="outline" @click="showMoreTeams">{{ $t('ui.loadMore') }}</Button></div>
        </CardContent>
      </Card>
      </template>
    </template>

    <component :is="LazyScoreboardTeamDetailDialog"
      v-model:open="teamDetailOpen"
      :mode="board.schema.value?.mode"
      :team="teamDetailTeam"
      :teams="teams"
      :column-groups="columnGroups"
      :trend-series="teamDetailTrendSeries"
      :trend-loading="trendsLoading"
      :trend-error="trendsError"
      :trend-range-start="trendRangeStart"
      :trend-range-end="trendRangeEnd"
      :trend-revision="trendRevision"
      @retry-trends="loadTrends"
    />

    <Dialog v-model:open="detailOpen">
      <DialogScrollContent data-score-detail-dialog class="max-h-[85vh] sm:max-w-2xl">
        <DialogHeader><DialogTitle>{{ detailTeam ? displayTeamName(detailTeam) : '' }} · {{ board.challengesById.value.get(detailColumn?.competitionChallengeId ?? '')?.title ?? $t('ui.unknownQuestion') }} · {{ detailColumn ? roundLabel(detailColumn) : '' }}</DialogTitle><DialogDescription>{{ isCtf ? $t('ui.scoresComeFromTheAuthoritativeServerSideScoringResult') : $t('ui.scoresAndStatesComeFromAuthoritativeServerSettlement') }}</DialogDescription></DialogHeader>
        <Alert v-if="detailError" variant="destructive"><AlertDescription>{{ $message(detailError) }}</AlertDescription></Alert>
        <div v-if="detailLoading" class="flex items-center justify-center py-10"><Spinner /></div>
        <template v-else-if="detail">
          <div class="grid gap-3" :class="isCtf ? 'grid-cols-2' : 'grid-cols-3'">
            <div v-if="!isCtf" class="rounded-lg border p-3"><p class="text-xs text-muted-foreground">{{ $t('ui.status') }}</p><p class="mt-1 font-medium">{{ slotTitle(detail) }}</p></div>
            <div class="rounded-lg border p-3"><p class="text-xs text-muted-foreground">{{ $t('ui.earned') }}</p><p class="mt-1 font-mono font-semibold">{{ detail.earnedPoints ?? $t('ui.symbol') }}</p></div>
            <div class="rounded-lg border p-3"><p class="text-xs text-muted-foreground">{{ $t('ui.netScore') }}</p><p class="mt-1 font-mono font-semibold">{{ detail.netPoints ?? $t('ui.symbol') }}</p></div>
          </div>
          <div v-if="detail.breakdown?.length" class="grid gap-px overflow-hidden rounded-lg border bg-border sm:grid-cols-2"><div v-for="item in detail.breakdown" :key="item.kind" class="flex items-center justify-between gap-4 bg-background p-3 text-sm"><div><p class="font-medium">{{ scoreboardEntryKindLabel(item.kind) }}</p><p class="text-xs text-muted-foreground">{{ $t('ui.succeededSubmitted', { success: item.successfulCount ?? 0, attempt: item.attemptCount ?? 0 }) }}</p></div><span class="font-mono font-semibold tabular-nums">{{ item.netPoints ?? 0 }} {{ $t('ui.pts2') }}</span></div></div>
          <div class="flex flex-col gap-2"><div v-for="entry in detailEntries" :key="entry.id" class="flex items-start justify-between gap-4 rounded-lg border p-3 text-sm"><div><p class="font-medium">{{ scoreboardEntryKindLabel(entry.kind) }} · {{ scoreboardEntryOutcomeLabel(entry.outcome) }}</p><p class="text-xs text-muted-foreground">{{ entryActor(entry) }} · {{ formatDateTime(entry.occurredAt) }}</p></div><span class="font-mono tabular-nums">{{ entry.netPoints ?? $t('ui.symbol') }}<template v-if="entry.netPoints !== null && entry.netPoints !== undefined"> {{ $t('ui.pts2') }}</template></span></div><p v-if="!detailEntries.length" class="py-6 text-center text-sm text-muted-foreground">{{ $t('ui.noDetails') }}</p></div>
          <Button v-if="detail.nextCursor" variant="outline" :disabled="detailLoadingMore" @click="loadDetailPage(detail.nextCursor ?? null, true)"><Spinner v-if="detailLoadingMore" />{{ $t('ui.loadMore') }}</Button>
        </template>
      </DialogScrollContent>
    </Dialog>

    <Dialog v-model:open="adjustmentOpen">
      <DialogScrollContent class="max-h-[85vh] sm:max-w-xl">
        <DialogHeader><DialogTitle>{{ adjustmentTeam ? displayTeamName(adjustmentTeam) : '' }} · {{ $t('ui.globalAdjustments') }}</DialogTitle><DialogDescription>{{ $t('ui.theCompleteAdjustmentHistoryComesFromAuthoritativeServerFacts') }}</DialogDescription></DialogHeader>
        <Alert v-if="adjustmentError" variant="destructive"><AlertDescription>{{ $message(adjustmentError) }}</AlertDescription></Alert>
        <div v-if="adjustmentLoading" class="flex items-center justify-center py-10"><Spinner /></div>
        <template v-else-if="adjustmentDetail">
          <div class="flex flex-col gap-2"><div v-for="entry in adjustmentEntries" :key="entry.id" class="flex items-start justify-between gap-4 rounded-lg border p-3 text-sm"><div><p class="font-medium">{{ adjustmentKind(entry) }}</p><p class="text-xs text-muted-foreground">{{ adjustmentActor(entry) }} · {{ formatDateTime(entry.occurredAt) }}</p></div><span class="font-mono font-semibold tabular-nums" :class="(entry.netPoints ?? 0) < 0 ? 'text-destructive' : 'text-emerald-600'">{{ (entry.netPoints ?? 0) > 0 ? '+' : '' }}{{ entry.netPoints ?? 0 }} {{ $t('ui.pts2') }}</span></div><p v-if="!adjustmentEntries.length" class="py-6 text-center text-sm text-muted-foreground">{{ $t('ui.noDetails') }}</p></div>
          <Button v-if="adjustmentDetail.nextCursor" variant="outline" :disabled="adjustmentLoadingMore" @click="loadAdjustmentPage(adjustmentDetail.nextCursor ?? null, true)"><Spinner v-if="adjustmentLoadingMore" />{{ $t('ui.loadMore') }}</Button>
        </template>
      </DialogScrollContent>
    </Dialog>
    </div>
  </ScrollSurface>
</template>
