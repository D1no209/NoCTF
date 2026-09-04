<script setup lang="ts">
import { ArrowLeft, ChevronLeft, ChevronRight, Download, History, Medal, Trophy } from '@lucide/vue'
import { getLeaderboardTrendsEndpoint, getScoreboardAdjustmentDetailEndpoint, getScoreboardSlotDetailEndpoint } from '~/api'
import type {
  NoCtfapiEndpointsCompetitionsScoreboardAdjustmentDetailResponse,
  NoCtfapiEndpointsCompetitionsScoreboardAdjustmentResponse,
  NoCtfapiEndpointsCompetitionsScoreboardColumnResponse,
  NoCtfapiEndpointsCompetitionsScoreboardEntryResponse,
  NoCtfapiEndpointsCompetitionsScoreboardSlotDetailResponse,
  NoCtfapiEndpointsCompetitionsScoreboardSlotResponse,
  NoCtfapiEndpointsCompetitionsScoreboardTeamResponse,
  NoCtfapiEndpointsCompetitionsScoreboardTrendsResponse,
} from '~/api'
import { medalBloodRankClass, medalRankClass } from '~/components/leaderboard/types'
import type { TrendSeries } from '~/components/leaderboard/types'
import {
  scoreboardChallengeColumnGroups,
  scoreboardBloodAward,
  scoreboardBreakdown,
  scoreboardEntryKindLabel,
  scoreboardEntryOutcomeLabel,
  scoreboardRankingStateLabel,
  scoreboardSlot,
} from '~/utils/scoreboard'

const route = useRoute()
const competitionId = route.params.id as string
const ctx = inject(competitionContextKey)!
const board = useScoreboardMatrix(competitionId)
const { isAdministrator } = useAuth()

const allTracksKey = '__all_tracks__'
const selectedTrackKey = ref(allTracksKey)
const visibleTeamCount = ref(50)
const canViewInternalTracks = computed(() => {
  const role = ctx.competition.value?.administrationRole
  return isAdministrator.value || role === 'Owner' || role === 'Manager' || role === 'Judge'
})
const availableTracks = computed(() => (board.snapshot.value?.tracks ?? [])
  .filter(track => canViewInternalTracks.value
    || !track.isInternal && (track.isViewerTrack || track.visibleOnLeaderboard)))
const selectedAllTracks = computed(() => selectedTrackKey.value === allTracksKey)
const trackNames = computed(() => new Map(availableTracks.value.map(track => [track.key, track.name])))
const trackName = (trackKey: string | undefined) => trackNames.value.get(trackKey) ?? trackKey ?? '-'

watch(availableTracks, (tracks) => {
  if (!tracks.length) {
    selectedTrackKey.value = allTracksKey
    return
  }
  if (selectedAllTracks.value) return
  if (!tracks.some(track => track.key === selectedTrackKey.value))
    selectedTrackKey.value = allTracksKey
}, { immediate: true })

const teams = computed(() => {
  const all = board.snapshot.value?.teams ?? []
  if (!selectedAllTracks.value)
    return all.filter(team => team.trackKey === selectedTrackKey.value)
  return [...all].sort((left, right) => {
    const leftEligible = left.rankingState === 'Eligible' ? 0 : 1
    const rightEligible = right.rankingState === 'Eligible' ? 0 : 1
    return leftEligible - rightEligible
      || (right.totalScore ?? 0) - (left.totalScore ?? 0)
      || (left.teamName ?? '').localeCompare(right.teamName ?? '')
      || (left.teamId ?? '').localeCompare(right.teamId ?? '')
  })
})
const displayRanks = computed(() => {
  if (!selectedAllTracks.value)
    return new Map(teams.value.map(team => [team.teamId, team.rank ?? null]))
  let rank = 0
  return new Map(teams.value.map((team) => {
    if (team.rankingState !== 'Eligible') return [team.teamId, null] as const
    rank += 1
    return [team.teamId, rank] as const
  }))
})
const displayRank = (team: NoCtfapiEndpointsCompetitionsScoreboardTeamResponse) =>
  displayRanks.value.get(team.teamId) ?? null
const visibleTeams = computed(() => teams.value.slice(0, visibleTeamCount.value))
const teamDisplayNames = computed(() => buildTeamDisplayNames(teams.value))
const displayTeamName = (team: NoCtfapiEndpointsCompetitionsScoreboardTeamResponse) =>
  teamDisplayName(team, teamDisplayNames.value)
watch(teams, () => { visibleTeamCount.value = 50 })

const columnGroups = computed(() => scoreboardChallengeColumnGroups(
  board.schema.value,
  board.catalog.value?.items,
))
const isCtf = computed(() => board.schema.value?.mode === 'Ctf')
const trends = ref<NoCtfapiEndpointsCompetitionsScoreboardTrendsResponse | null>(null)
const trendsLoading = ref(false)
const trendsError = ref<string | null>(null)
let trendsGeneration = 0
let trendsRetryTimer: ReturnType<typeof setTimeout> | null = null

const visibleTrendSeries = computed<TrendSeries[]>(() => {
  const trendsByTeamId = new Map((trends.value?.teams ?? [])
    .filter(team => team.teamId)
    .map(team => [team.teamId!, team]))
  return teams.value
    .filter(team => team.teamId && trendsByTeamId.has(team.teamId))
    .map((team) => {
      const trend = trendsByTeamId.get(team.teamId!)!
      return {
        teamId: team.teamId,
        teamName: displayTeamName(team),
        points: trend.points ?? [],
      }
    })
})
const trendRangeStart = computed(() => ctx.competition.value?.startTime ?? null)
const trendRangeEnd = computed(() => trends.value?.dataAsOf ?? trends.value?.generatedAt ?? null)

async function loadTrends(): Promise<void> {
  if (trendsRetryTimer) clearTimeout(trendsRetryTimer)
  trendsRetryTimer = null
  if (!isCtf.value || board.snapshot.value?.dataScope === 'Hidden') {
    trendsGeneration += 1
    trends.value = null
    trendsError.value = null
    trendsLoading.value = false
    return
  }
  const requestGeneration = ++trendsGeneration
  trendsLoading.value = true
  try {
    const result = await getLeaderboardTrendsEndpoint({ path: { competitionId } })
    if (requestGeneration !== trendsGeneration) return
    if (result.error) {
      trendsError.value = parseApiError(result.error, translate('加载得分趋势失败')).message
      return
    }
    if (result.response?.status === 202) {
      trendsRetryTimer = setTimeout(() => {
        trendsRetryTimer = null
        void loadTrends()
      }, 2000)
      return
    }
    if (result.data) {
      trends.value = result.data as NoCtfapiEndpointsCompetitionsScoreboardTrendsResponse
      trendsError.value = null
    }
  }
  catch (error) {
    if (requestGeneration === trendsGeneration)
      trendsError.value = parseApiError(error, translate('加载得分趋势失败')).message
  }
  finally {
    if (requestGeneration === trendsGeneration)
      trendsLoading.value = false
  }
}

watch(
  [isCtf, () => board.snapshot.value?.version ?? null, () => board.snapshot.value?.dataScope ?? null],
  () => void loadTrends(),
  { immediate: true },
)

onBeforeUnmount(() => {
  trendsGeneration += 1
  if (trendsRetryTimer) clearTimeout(trendsRetryTimer)
  trendsRetryTimer = null
})
const missingChallengeIds = computed(() => columnGroups.value
  .filter(group => !group.challenge)
  .map(group => group.competitionChallengeId))
let missingCatalogRefreshRevision: string | null = null
watch(
  [() => board.schema.value?.challengeCatalogRevision ?? null, missingChallengeIds],
  ([revision, challengeIds]) => {
    if (!revision || challengeIds.length === 0 || missingCatalogRefreshRevision === revision) return
    missingCatalogRefreshRevision = revision
    void board.refresh({ catalog: true, schema: false, snapshot: false })
  },
  { flush: 'post' },
)
const flatColumns = computed(() => columnGroups.value.flatMap(group => group.columns))
const roundWindowLabel = computed(() => {
  const start = board.schema.value?.roundWindowStart
  const end = board.schema.value?.roundWindowEnd
  if (!start || !end) return translate('暂无已结算轮次')
  return translate('第 {start}–{end} 轮', { start, end })
})

function roundLabel(column: NoCtfapiEndpointsCompetitionsScoreboardColumnResponse): string {
  if (!column.roundId) return translate('总计')
  const round = board.roundsById.value.get(column.roundId)
  return round?.number ? translate('第 {round} 轮', { round: round.number }) : translate('轮次')
}

function slotTitle(slot: NoCtfapiEndpointsCompetitionsScoreboardSlotResponse | NoCtfapiEndpointsCompetitionsScoreboardSlotDetailResponse): string {
  if (slot.scoreState === 'Pending') return translate('本轮待结算')
  if (slot.scoreState === 'Provisional') return translate('结算中')
  return translate('已结算')
}

function ctfScore(slot: NoCtfapiEndpointsCompetitionsScoreboardSlotResponse | null): number | null {
  if ((scoreboardBreakdown(slot, 'Solve')?.successfulCount ?? 0) < 1) return null
  return slot?.netPoints ?? 0
}

function exportCsv(): void {
  const snapshot = board.snapshot.value
  if (!snapshot) return
  const header = [
    translate('名次'),
    translate('队伍'),
    ...(selectedAllTracks.value ? [translate('赛道')] : []),
    translate('总分'),
    ...flatColumns.value.map((column) => {
      const challenge = board.challengesById.value.get(column.competitionChallengeId ?? '')
      return `${challenge?.title ?? translate('未知题目')} · ${roundLabel(column)}`
    }),
  ]
  const rows = teams.value.map(team => [
    displayRank(team) ?? '',
    displayTeamName(team),
    ...(selectedAllTracks.value ? [trackName(team.trackKey)] : []),
    team.totalScore ?? 0,
    ...flatColumns.value.map((column) => {
      if (column.index === undefined) return ''
      const slot = scoreboardSlot(team, column.index)
      return slot?.scoreState === 'Settled' ? slot.netPoints ?? 0 : ''
    }),
  ])
  const escape = (value: unknown) => `"${String(value).replaceAll('"', '""')}"`
  const csv = '\uFEFF' + [header, ...rows].map(row => row.map(escape).join(',')).join('\r\n')
  const url = URL.createObjectURL(new Blob([csv], { type: 'text/csv;charset=utf-8' }))
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = `${ctx.competition.value?.title ?? 'scoreboard'}-${translate('记分板')}.csv`
  anchor.click()
  URL.revokeObjectURL(url)
}

const detailOpen = ref(false)
const teamDetailOpen = ref(false)
const teamDetailTeam = ref<NoCtfapiEndpointsCompetitionsScoreboardTeamResponse | null>(null)
const teamDetailTrendSeries = computed(() => {
  const teamId = teamDetailTeam.value?.teamId
  if (!teamId) return []
  return visibleTrendSeries.value.filter(series => series.teamId === teamId)
})
const detailLoading = ref(false)
const detailLoadingMore = ref(false)
const detailError = ref<string | null>(null)
const detail = ref<NoCtfapiEndpointsCompetitionsScoreboardSlotDetailResponse | null>(null)
const detailEntries = ref<NoCtfapiEndpointsCompetitionsScoreboardEntryResponse[]>([])
const detailActorNames = ref(new Map<string, string>())
const detailTeam = ref<NoCtfapiEndpointsCompetitionsScoreboardTeamResponse | null>(null)
const detailColumn = ref<NoCtfapiEndpointsCompetitionsScoreboardColumnResponse | null>(null)
let detailGeneration = 0

async function loadDetailPage(cursor: string | null, append: boolean): Promise<void> {
  const teamId = detailTeam.value?.teamId
  const columnIndex = detailColumn.value?.index
  if (!teamId || columnIndex === undefined) return
  const requestGeneration = detailGeneration
  if (append) detailLoadingMore.value = true
  else detailLoading.value = true
  try {
    const result = await getScoreboardSlotDetailEndpoint({
      path: { competitionId, teamId, columnIndex },
      query: { cursor, limit: 50, endingRound: board.detailEndingRound.value },
    })
    if (requestGeneration !== detailGeneration) return
    if (result.error) {
      detailError.value = parseApiError(result.error, translate('加载记分板明细失败')).message
      return
    }
    if (result.response?.status === 202) {
      detailError.value = translate('记分板数据投影中,请稍候…')
      return
    }
    if (!result.data) return
    const page = result.data as NoCtfapiEndpointsCompetitionsScoreboardSlotDetailResponse
    const pageActors = new Map((page.actors ?? []).map(actor => [actor.index, actor.displayName]))
    const actorNames = append ? new Map(detailActorNames.value) : new Map<string, string>()
    for (const entry of page.items ?? []) {
      if (entry.actorIndex !== null && entry.actorIndex !== undefined) {
        const displayName = pageActors.get(entry.actorIndex)
        if (displayName && entry.id) actorNames.set(entry.id, displayName)
      }
    }
    detail.value = page
    detailEntries.value = append ? [...detailEntries.value, ...(page.items ?? [])] : [...(page.items ?? [])]
    detailActorNames.value = actorNames
    detailError.value = null
  }
  catch (error) {
    if (requestGeneration === detailGeneration)
      detailError.value = parseApiError(error, translate('加载记分板明细失败')).message
  }
  finally {
    if (requestGeneration === detailGeneration) {
      detailLoading.value = false
      detailLoadingMore.value = false
    }
  }
}

function openDetail(team: NoCtfapiEndpointsCompetitionsScoreboardTeamResponse, column: NoCtfapiEndpointsCompetitionsScoreboardColumnResponse): void {
  detailGeneration += 1
  detailTeam.value = team
  detailColumn.value = column
  detail.value = null
  detailEntries.value = []
  detailActorNames.value = new Map()
  detailError.value = null
  detailOpen.value = true
  void loadDetailPage(null, false)
}

function openTeamDetail(team: NoCtfapiEndpointsCompetitionsScoreboardTeamResponse): void {
  teamDetailTeam.value = team
  teamDetailOpen.value = true
}

watch(detailOpen, (open) => { if (!open) detailGeneration += 1 })

async function showOlderRoundWindow(): Promise<void> {
  detailOpen.value = false
  detailGeneration += 1
  await board.showOlderRounds()
}

async function showNewerRoundWindow(): Promise<void> {
  detailOpen.value = false
  detailGeneration += 1
  await board.showNewerRounds()
}

async function showLatestRoundWindow(): Promise<void> {
  detailOpen.value = false
  detailGeneration += 1
  await board.showLatestRounds()
}

function entryActor(entry: NoCtfapiEndpointsCompetitionsScoreboardEntryResponse): string {
  if (entry.actorIndex === null || entry.actorIndex === undefined) return translate('系统')
  return (entry.id ? detailActorNames.value.get(entry.id) : null) ?? translate('未知用户')
}

const adjustmentOpen = ref(false)
const adjustmentLoading = ref(false)
const adjustmentLoadingMore = ref(false)
const adjustmentError = ref<string | null>(null)
const adjustmentDetail = ref<NoCtfapiEndpointsCompetitionsScoreboardAdjustmentDetailResponse | null>(null)
const adjustmentEntries = ref<NoCtfapiEndpointsCompetitionsScoreboardAdjustmentResponse[]>([])
const adjustmentActorNames = ref(new Map<string, string>())
const adjustmentTeam = ref<NoCtfapiEndpointsCompetitionsScoreboardTeamResponse | null>(null)
let adjustmentGeneration = 0

async function loadAdjustmentPage(cursor: string | null, append: boolean): Promise<void> {
  const teamId = adjustmentTeam.value?.teamId
  if (!teamId) return
  const requestGeneration = adjustmentGeneration
  if (append) adjustmentLoadingMore.value = true
  else adjustmentLoading.value = true
  try {
    const result = await getScoreboardAdjustmentDetailEndpoint({
      path: { competitionId, teamId },
      query: { cursor, limit: 50 },
    })
    if (requestGeneration !== adjustmentGeneration) return
    if (result.error) {
      adjustmentError.value = parseApiError(result.error, translate('加载全局调分明细失败')).message
      return
    }
    if (result.response?.status === 202) {
      adjustmentError.value = translate('记分板数据投影中,请稍候…')
      return
    }
    if (!result.data) return
    const page = result.data as NoCtfapiEndpointsCompetitionsScoreboardAdjustmentDetailResponse
    const pageActors = new Map((page.actors ?? []).map(actor => [actor.index, actor.displayName]))
    const actorNames = append ? new Map(adjustmentActorNames.value) : new Map<string, string>()
    for (const entry of page.items ?? []) {
      if (entry.actorIndex !== null && entry.actorIndex !== undefined) {
        const displayName = pageActors.get(entry.actorIndex)
        if (displayName && entry.id) actorNames.set(entry.id, displayName)
      }
    }
    adjustmentDetail.value = page
    adjustmentEntries.value = append ? [...adjustmentEntries.value, ...(page.items ?? [])] : [...(page.items ?? [])]
    adjustmentActorNames.value = actorNames
    adjustmentError.value = null
  }
  catch (error) {
    if (requestGeneration === adjustmentGeneration)
      adjustmentError.value = parseApiError(error, translate('加载全局调分明细失败')).message
  }
  finally {
    if (requestGeneration === adjustmentGeneration) {
      adjustmentLoading.value = false
      adjustmentLoadingMore.value = false
    }
  }
}

function openAdjustments(team: NoCtfapiEndpointsCompetitionsScoreboardTeamResponse): void {
  adjustmentGeneration += 1
  adjustmentTeam.value = team
  adjustmentDetail.value = null
  adjustmentEntries.value = []
  adjustmentActorNames.value = new Map()
  adjustmentError.value = null
  adjustmentOpen.value = true
  void loadAdjustmentPage(null, false)
}

watch(adjustmentOpen, (open) => { if (!open) adjustmentGeneration += 1 })

function adjustmentActor(entry: NoCtfapiEndpointsCompetitionsScoreboardAdjustmentResponse): string {
  if (entry.actorIndex === null || entry.actorIndex === undefined) return translate('系统')
  return (entry.id ? adjustmentActorNames.value.get(entry.id) : null) ?? translate('未知用户')
}

function adjustmentKind(entry: NoCtfapiEndpointsCompetitionsScoreboardAdjustmentResponse): string {
  if (entry.kind === 'CompetitionPenalty') return translate('比赛处罚')
  if (entry.kind === 'BanRecalculation') return translate('禁赛重算')
  return translate('人工调分')
}
</script>

<template>
  <div class="flex flex-col gap-6">
    <div>
      <Button variant="ghost" size="sm" as-child>
        <NuxtLink :to="`/competitions/${competitionId}`">
          <ArrowLeft data-icon="inline-start" />{{ $t('返回比赛') }}
        </NuxtLink>
      </Button>
    </div>
    <Alert v-if="board.error.value" variant="destructive">
      <AlertDescription class="flex items-center justify-between gap-3"><span>{{ board.error.value }}</span><Button variant="outline" size="sm" @click="board.refresh()">{{ $t('重试') }}</Button></AlertDescription>
    </Alert>
    <div v-if="board.loading.value && !board.snapshot.value" class="flex flex-col gap-4">
      <Alert><AlertDescription class="flex items-center gap-2"><Spinner class="size-3" />{{ $t('记分板数据投影中,请稍候…') }}</AlertDescription></Alert>
      <Skeleton class="h-64 w-full" />
    </div>
    <template v-else-if="board.snapshot.value && board.schema.value && board.catalog.value">
      <div class="flex flex-wrap items-center justify-between gap-3">
        <h2 class="flex items-center gap-2 text-display text-xl"><Trophy class="size-5 text-primary" />{{ $t('排行榜') }}<Badge v-if="board.refreshing.value" variant="secondary">{{ $t('刷新中') }}</Badge></h2>
        <div class="flex flex-wrap items-center gap-3">
          <div v-if="board.schema.value.mode === 'Awdp' || board.schema.value.mode === 'Awd'" class="flex flex-wrap items-center gap-2">
            <Badge variant="outline" class="font-mono tabular-nums">{{ roundWindowLabel }}</Badge>
            <Button variant="outline" size="sm" :disabled="board.refreshing.value || !board.canShowOlderRounds.value" @click="showOlderRoundWindow">
              <ChevronLeft data-icon="inline-start" />{{ $t('较早轮次') }}
            </Button>
            <Button variant="outline" size="sm" :disabled="board.refreshing.value || !board.canShowNewerRounds.value" @click="showNewerRoundWindow">
              {{ $t('较新轮次') }}<ChevronRight data-icon="inline-end" />
            </Button>
            <Button v-if="!board.viewingLatestRounds.value" variant="outline" size="sm" :disabled="board.refreshing.value" @click="showLatestRoundWindow">
              <History data-icon="inline-start" />{{ $t('返回最新轮次') }}
            </Button>
          </div>
          <Select v-if="availableTracks.length > 1" v-model="selectedTrackKey">
            <SelectTrigger class="min-w-40" :aria-label="$t('选择排行榜赛道')"><SelectValue /></SelectTrigger>
            <SelectContent>
              <SelectItem :value="allTracksKey">{{ $t('所有赛道') }}</SelectItem>
              <SelectItem v-for="track in availableTracks" :key="track.key" :value="track.key!">{{ track.name }}</SelectItem>
            </SelectContent>
          </Select>
          <Button variant="outline" :disabled="!teams.length" @click="exportCsv"><Download data-icon="inline-start" />{{ $t('下载为Excel') }}</Button>
        </div>
      </div>
      <Alert v-if="board.processing.value"><AlertDescription>{{ $t('记分板数据投影中,请稍候…') }}</AlertDescription></Alert>
      <Alert v-if="board.snapshot.value.dataScope === 'Frozen'"><AlertDescription>{{ $t('排行榜已冻结，以下为截至 {time} 的快照。', { time: formatDateTime(board.snapshot.value.dataAsOf) }) }}</AlertDescription></Alert>
      <Empty v-if="board.snapshot.value.dataScope === 'Hidden'" class="border py-12"><EmptyHeader><EmptyTitle>{{ $t('排行榜暂不公开') }}</EmptyTitle><EmptyDescription>{{ $t('主办方当前隐藏了排行榜数据') }}</EmptyDescription></EmptyHeader></Empty>
      <template v-else>
        <Alert v-if="isCtf && trendsError" variant="destructive"><AlertDescription class="flex items-center justify-between gap-3"><span>{{ trendsError }}</span><Button variant="outline" size="sm" @click="loadTrends">{{ $t('重试') }}</Button></AlertDescription></Alert>
        <div v-if="isCtf">
          <Card>
            <CardHeader class="pb-0">
              <CardTitle class="text-base">{{ $t('总分趋势') }}</CardTitle>
              <CardDescription>{{ $t('当前赛道全部队伍的累计分值变化') }}</CardDescription>
            </CardHeader>
            <CardContent class="pt-2">
              <Skeleton v-if="trendsLoading && !trends" class="h-[320px] w-full" />
              <Empty v-else-if="!visibleTrendSeries.length" class="h-[320px]"><EmptyHeader><EmptyTitle>{{ $t('暂无得分趋势') }}</EmptyTitle></EmptyHeader></Empty>
              <LazyScoreTrendChart v-else :series="visibleTrendSeries" :range-start="trendRangeStart" :range-end="trendRangeEnd" height="320px" />
            </CardContent>
          </Card>
        </div>
      <Card>
        <CardContent class="pt-6">
          <Empty v-if="!teams.length" class="border py-8"><EmptyHeader><EmptyTitle>{{ $t('还没有队伍得分') }}</EmptyTitle></EmptyHeader></Empty>
          <div v-else class="min-w-0">
            <Table class="min-w-max table-auto">
              <TableHeader>
                <TableRow>
                  <TableHead :rowspan="isCtf ? 1 : 2" class="sticky left-0 z-30 w-20 min-w-20 max-w-20 bg-card text-center">{{ $t('名次') }}</TableHead>
                  <TableHead :rowspan="isCtf ? 1 : 2" class="sticky left-20 z-30 w-56 min-w-56 max-w-56 bg-card">{{ $t('参赛队伍') }}</TableHead>
                  <TableHead :rowspan="isCtf ? 1 : 2" class="sticky left-76 z-30 w-28 min-w-28 max-w-28 border-r bg-card text-right">{{ $t('总分') }}</TableHead>
                  <TableHead v-for="group in columnGroups" :key="group.competitionChallengeId" :colspan="group.columns.length" class="border-l px-4 text-center">
                    <span class="inline-flex items-center gap-1.5 whitespace-nowrap"><component :is="directionIcon(group.challenge?.direction)" class="size-4" :class="directionTextClass(group.challenge?.direction)" />{{ group.challenge?.title ?? $t('未知题目') }}</span>
                  </TableHead>
                </TableRow>
                <TableRow v-if="!isCtf"><template v-for="group in columnGroups" :key="`${group.competitionChallengeId}-rounds`"><TableHead v-for="column in group.columns" :key="column.index" class="min-w-28 border-l px-3 text-center">{{ roundLabel(column) }}</TableHead></template></TableRow>
              </TableHeader>
              <TableBody>
                <TableRow v-for="team in visibleTeams" :key="team.teamId" :class="(displayRank(team) ?? 99) <= 3 ? 'bg-primary/5' : ''">
                  <TableCell class="sticky left-0 z-20 w-20 min-w-20 max-w-20 bg-card text-center"><Medal v-if="(displayRank(team) ?? 99) <= 3" class="size-5" :class="medalRankClass[displayRank(team) ?? 0]" /><span v-else class="font-mono tabular-nums">{{ displayRank(team) ?? '—' }}</span></TableCell>
                  <TableCell class="sticky left-20 z-20 w-56 min-w-56 max-w-56 bg-card"><div class="flex min-w-0 flex-col items-start gap-1"><button type="button" class="w-full whitespace-normal break-words rounded-sm text-left font-medium underline-offset-4 hover:text-primary hover:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring" :title="displayTeamName(team)" :aria-label="$t('查看队伍 {team} 详情', { team: displayTeamName(team) })" @click="openTeamDetail(team)">{{ displayTeamName(team) }}</button><div v-if="(selectedAllTracks && availableTracks.length > 1) || team.rankingState !== 'Eligible'" class="flex flex-wrap items-center gap-1"><Badge v-if="selectedAllTracks && availableTracks.length > 1" variant="outline" class="whitespace-normal break-words text-left" :title="trackName(team.trackKey)">{{ trackName(team.trackKey) }}</Badge><Badge v-if="team.rankingState !== 'Eligible'" variant="destructive">{{ scoreboardRankingStateLabel(team.rankingState) }}</Badge></div></div></TableCell>
                  <TableCell class="sticky left-76 z-20 w-28 min-w-28 max-w-28 border-r bg-card text-right">
                    <button v-if="(team.globalAdjustmentCount ?? 0) > 0" type="button" class="w-full rounded-md px-2 py-1 text-right transition-colors hover:bg-muted focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring" @click="openAdjustments(team)">
                      <span class="block font-mono font-semibold tabular-nums">{{ team.totalScore ?? 0 }} pts</span>
                      <span class="mt-1 block text-[0.7rem] text-muted-foreground">{{ $t('全局调分 {count} 条', { count: team.globalAdjustmentCount ?? 0 }) }}</span>
                    </button>
                    <span v-else class="font-mono font-semibold tabular-nums">{{ team.totalScore ?? 0 }} pts</span>
                  </TableCell>
                  <template v-for="group in columnGroups" :key="`${team.teamId}-${group.competitionChallengeId}`">
                    <TableCell v-for="column in group.columns" :key="column.index" class="border-l p-1 text-center" :class="isCtf ? 'min-w-56' : 'min-w-28'">
                      <button v-if="column.index !== undefined && scoreboardSlot(team, column.index)" type="button" class="flex min-h-12 w-full items-center justify-center rounded-md px-1 py-1 transition-colors hover:bg-muted focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring" :aria-label="$t('查看 {team} 在 {challenge} {round} 的详情', { team: displayTeamName(team), challenge: group.challenge?.title ?? $t('未知题目'), round: roundLabel(column) })" @click="openDetail(team, column)">
                        <template v-if="isCtf">
                          <span v-if="ctfScore(scoreboardSlot(team, column.index!)) !== null" class="inline-flex items-center justify-center gap-2 whitespace-nowrap">
                            <span class="font-mono text-lg font-bold tabular-nums">{{ ctfScore(scoreboardSlot(team, column.index!)) }} pts</span>
                            <span
                              v-if="scoreboardBloodAward(scoreboardSlot(team, column.index!))"
                              class="inline-flex items-center gap-1 text-primary"
                              :title="`${scoreboardBloodAward(scoreboardSlot(team, column.index!))?.label} +${scoreboardBloodAward(scoreboardSlot(team, column.index!))?.points} pts`"
                            >
                              <Medal
                                class="size-4 shrink-0"
                                :class="medalBloodRankClass(scoreboardBloodAward(scoreboardSlot(team, column.index!))?.award)"
                                aria-hidden="true"
                              />
                              <span class="sr-only">{{ scoreboardBloodAward(scoreboardSlot(team, column.index!))?.label }}</span>
                              <span class="font-mono text-xs font-semibold tabular-nums">+{{ scoreboardBloodAward(scoreboardSlot(team, column.index!))?.points }} pts</span>
                            </span>
                          </span>
                          <span v-else class="font-mono text-sm text-muted-foreground/60">-</span>
                        </template>
                        <ScoreboardSlotStatus v-else :mode="board.schema.value.mode" :slot="scoreboardSlot(team, column.index!)!" />
                      </button>
                      <span v-else class="font-mono text-sm text-muted-foreground/60">-</span>
                    </TableCell>
                  </template>
                </TableRow>
              </TableBody>
            </Table>
          </div>
          <div v-if="visibleTeams.length < teams.length" class="mt-4 flex justify-center"><Button variant="outline" @click="visibleTeamCount += 50">{{ $t('加载更多') }}</Button></div>
        </CardContent>
      </Card>
      </template>
    </template>

    <LazyScoreboardTeamDetailDialog
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
      @retry-trends="loadTrends"
    />

    <Dialog v-model:open="detailOpen">
      <DialogScrollContent class="max-h-[85vh] sm:max-w-2xl">
        <DialogHeader><DialogTitle>{{ detailTeam ? displayTeamName(detailTeam) : '' }} · {{ board.challengesById.value.get(detailColumn?.competitionChallengeId ?? '')?.title ?? $t('未知题目') }} · {{ detailColumn ? roundLabel(detailColumn) : '' }}</DialogTitle><DialogDescription>{{ isCtf ? $t('分值来自服务端权威计分结果。') : $t('分值与状态均来自服务端权威结算结果。') }}</DialogDescription></DialogHeader>
        <Alert v-if="detailError" variant="destructive"><AlertDescription>{{ detailError }}</AlertDescription></Alert>
        <div v-if="detailLoading" class="flex items-center justify-center py-10"><Spinner /></div>
        <template v-else-if="detail">
          <div class="grid gap-3" :class="isCtf ? 'grid-cols-2' : 'grid-cols-3'">
            <div v-if="!isCtf" class="rounded-lg border p-3"><p class="text-xs text-muted-foreground">{{ $t('状态') }}</p><p class="mt-1 font-medium">{{ slotTitle(detail) }}</p></div>
            <div class="rounded-lg border p-3"><p class="text-xs text-muted-foreground">{{ $t('得分') }}</p><p class="mt-1 font-mono font-semibold">{{ detail.earnedPoints ?? '—' }}</p></div>
            <div class="rounded-lg border p-3"><p class="text-xs text-muted-foreground">{{ $t('净分') }}</p><p class="mt-1 font-mono font-semibold">{{ detail.netPoints ?? '—' }}</p></div>
          </div>
          <div v-if="detail.breakdown?.length" class="grid gap-px overflow-hidden rounded-lg border bg-border sm:grid-cols-2"><div v-for="item in detail.breakdown" :key="item.kind" class="flex items-center justify-between gap-4 bg-background p-3 text-sm"><div><p class="font-medium">{{ scoreboardEntryKindLabel(item.kind) }}</p><p class="text-xs text-muted-foreground">{{ $t('成功 {success} / 提交 {attempt}', { success: item.successfulCount ?? 0, attempt: item.attemptCount ?? 0 }) }}</p></div><span class="font-mono font-semibold tabular-nums">{{ item.netPoints ?? 0 }} pts</span></div></div>
          <div class="flex flex-col gap-2"><div v-for="entry in detailEntries" :key="entry.id" class="flex items-start justify-between gap-4 rounded-lg border p-3 text-sm"><div><p class="font-medium">{{ scoreboardEntryKindLabel(entry.kind) }} · {{ scoreboardEntryOutcomeLabel(entry.outcome) }}</p><p class="text-xs text-muted-foreground">{{ entryActor(entry) }} · {{ formatDateTime(entry.occurredAt) }}</p></div><span class="font-mono tabular-nums">{{ entry.netPoints ?? '—' }}<template v-if="entry.netPoints !== null && entry.netPoints !== undefined"> pts</template></span></div><p v-if="!detailEntries.length" class="py-6 text-center text-sm text-muted-foreground">{{ $t('暂无明细') }}</p></div>
          <Button v-if="detail.nextCursor" variant="outline" :disabled="detailLoadingMore" @click="loadDetailPage(detail.nextCursor ?? null, true)"><Spinner v-if="detailLoadingMore" />{{ $t('加载更多') }}</Button>
        </template>
      </DialogScrollContent>
    </Dialog>

    <Dialog v-model:open="adjustmentOpen">
      <DialogScrollContent class="max-h-[85vh] sm:max-w-xl">
        <DialogHeader><DialogTitle>{{ adjustmentTeam ? displayTeamName(adjustmentTeam) : '' }} · {{ $t('全局调分') }}</DialogTitle><DialogDescription>{{ $t('完整调分记录均来自服务端权威事实。') }}</DialogDescription></DialogHeader>
        <Alert v-if="adjustmentError" variant="destructive"><AlertDescription>{{ adjustmentError }}</AlertDescription></Alert>
        <div v-if="adjustmentLoading" class="flex items-center justify-center py-10"><Spinner /></div>
        <template v-else-if="adjustmentDetail">
          <div class="flex flex-col gap-2"><div v-for="entry in adjustmentEntries" :key="entry.id" class="flex items-start justify-between gap-4 rounded-lg border p-3 text-sm"><div><p class="font-medium">{{ adjustmentKind(entry) }}</p><p class="text-xs text-muted-foreground">{{ adjustmentActor(entry) }} · {{ formatDateTime(entry.occurredAt) }}</p></div><span class="font-mono font-semibold tabular-nums" :class="(entry.netPoints ?? 0) < 0 ? 'text-destructive' : 'text-emerald-600'">{{ (entry.netPoints ?? 0) > 0 ? '+' : '' }}{{ entry.netPoints ?? 0 }} pts</span></div><p v-if="!adjustmentEntries.length" class="py-6 text-center text-sm text-muted-foreground">{{ $t('暂无明细') }}</p></div>
          <Button v-if="adjustmentDetail.nextCursor" variant="outline" :disabled="adjustmentLoadingMore" @click="loadAdjustmentPage(adjustmentDetail.nextCursor ?? null, true)"><Spinner v-if="adjustmentLoadingMore" />{{ $t('加载更多') }}</Button>
        </template>
      </DialogScrollContent>
    </Dialog>
  </div>
</template>
