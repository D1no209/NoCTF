import { defineAsyncComponent } from 'vue'
import { markRaw } from 'vue'

import { ArrowLeft, ChevronLeft, ChevronRight, Download, History, Medal, Trophy } from '@lucide/vue'
import { getLeaderboardTrendsEndpoint, getScoreboardAdjustmentDetailEndpoint, getScoreboardSlotDetailEndpoint } from '../../../../api'
import type { NoCtfapiEndpointsCompetitionsScoreboardAdjustmentDetailResponse, NoCtfapiEndpointsCompetitionsScoreboardAdjustmentResponse, NoCtfapiEndpointsCompetitionsScoreboardColumnResponse, NoCtfapiEndpointsCompetitionsScoreboardEntryResponse, NoCtfapiEndpointsCompetitionsScoreboardSlotDetailResponse, NoCtfapiEndpointsCompetitionsScoreboardSlotResponse, NoCtfapiEndpointsCompetitionsScoreboardTeamResponse, NoCtfapiEndpointsCompetitionsScoreboardTrendsResponse } from '../../../../api'
import { medalBloodRankClass, medalRankClass } from '../../../leaderboard/types'
import type { TrendSeries } from '../../../leaderboard/types'
import { scoreboardChallengeColumnGroups, scoreboardBloodAward, scoreboardBreakdown, scoreboardEntryKindLabel, scoreboardEntryOutcomeLabel, scoreboardRankingStateLabel, scoreboardSlot } from '../../../../utils/scoreboard'
import ScoreboardSlotStatusComponent from '../../../leaderboard/ScoreboardSlotStatus.vue'

/** Owns state, effects and commands for CompetitionsByIdLeaderboardPage. */
export function useCompetitionsByIdLeaderboardPage() {
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
        trendsError.value = parseApiError(result.error, translate("ui.failedToLoadScoreTrends")).message
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
        trendsError.value = parseApiError(error, translate("ui.failedToLoadScoreTrends")).message
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
    if (!start || !end) return translate("ui.noSettledRoundsYet")
    return translate("ui.rounds", { start, end })
  })

  function roundLabel(column: NoCtfapiEndpointsCompetitionsScoreboardColumnResponse): string {
    if (!column.roundId) return translate("ui.total2")
    const round = board.roundsById.value.get(column.roundId)
    return round?.number ? translate("ui.round2", { round: round.number }) : translate("ui.round3")
  }

  function slotTitle(slot: NoCtfapiEndpointsCompetitionsScoreboardSlotResponse | NoCtfapiEndpointsCompetitionsScoreboardSlotDetailResponse): string {
    if (slot.scoreState === 'Pending') return translate("ui.pendingRoundSettlement")
    if (slot.scoreState === 'Provisional') return translate("ui.settling")
    return translate("ui.settled")
  }

  function ctfScore(slot: NoCtfapiEndpointsCompetitionsScoreboardSlotResponse | null): number | null {
    if ((scoreboardBreakdown(slot, 'Solve')?.successfulCount ?? 0) < 1) return null
    return slot?.netPoints ?? 0
  }

  function exportCsv(): void {
    const snapshot = board.snapshot.value
    if (!snapshot) return
    const header = [
      translate("ui.ranking"),
      translate("ui.team"),
      ...(selectedAllTracks.value ? [translate("ui.tracks")] : []),
      translate("ui.totalScore"),
      ...flatColumns.value.map((column) => {
        const challenge = board.challengesById.value.get(column.competitionChallengeId ?? '')
        return `${challenge?.title ?? translate("ui.unknownQuestion")} · ${roundLabel(column)}`
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
    anchor.download = `${ctx.competition.value?.title ?? 'scoreboard'}-${translate("ui.leaderboard")}.csv`
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
        detailError.value = parseApiError(result.error, translate("ui.failedToLoadScoreboardDetails")).message
        return
      }
      if (result.response?.status === 202) {
        detailError.value = translate("ui.scoreboardDataIsBeingProjectedPleaseWait")
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
        detailError.value = parseApiError(error, translate("ui.failedToLoadScoreboardDetails")).message
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
    if (entry.actorIndex === null || entry.actorIndex === undefined) return translate("ui.system")
    return (entry.id ? detailActorNames.value.get(entry.id) : null) ?? translate("ui.unknownUser")
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
        adjustmentError.value = parseApiError(result.error, translate("ui.failedToLoadGlobalAdjustmentDetails")).message
        return
      }
      if (result.response?.status === 202) {
        adjustmentError.value = translate("ui.scoreboardDataIsBeingProjectedPleaseWait")
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
        adjustmentError.value = parseApiError(error, translate("ui.failedToLoadGlobalAdjustmentDetails")).message
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
    if (entry.actorIndex === null || entry.actorIndex === undefined) return translate("ui.system")
    return (entry.id ? adjustmentActorNames.value.get(entry.id) : null) ?? translate("ui.unknownUser")
  }

  function adjustmentKind(entry: NoCtfapiEndpointsCompetitionsScoreboardAdjustmentResponse): string {
    if (entry.kind === 'CompetitionPenalty') return translate("ui.competitionPenalty")
    if (entry.kind === 'BanRecalculation') return translate("ui.banRecalculation")
    return translate("ui.manualAdjustment")
  }

  const ScoreboardSlotStatus = markRaw(ScoreboardSlotStatusComponent)

  const LazyScoreTrendChart = markRaw(defineAsyncComponent(() => import('../../../leaderboard/ScoreTrendChart.vue')))

  const LazyScoreboardTeamDetailDialog = markRaw(defineAsyncComponent(() => import('../../../leaderboard/ScoreboardTeamDetailDialog.vue')))

  return {
      LazyScoreboardTeamDetailDialog,
      LazyScoreTrendChart,
      ArrowLeft,
      ChevronLeft,
      ChevronRight,
      Download,
      History,
      Medal,
      Trophy,
      medalBloodRankClass,
      medalRankClass,
      scoreboardBloodAward,
      scoreboardEntryKindLabel,
      scoreboardEntryOutcomeLabel,
      scoreboardRankingStateLabel,
      scoreboardSlot,
      competitionId,
      board,
      allTracksKey,
      selectedTrackKey,
      visibleTeamCount,
      availableTracks,
      selectedAllTracks,
      trackName,
      teams,
      displayRank,
      visibleTeams,
      displayTeamName,
      columnGroups,
      isCtf,
      trends,
      trendsLoading,
      trendsError,
      visibleTrendSeries,
      trendRangeStart,
      trendRangeEnd,
      loadTrends,
      roundWindowLabel,
      roundLabel,
      slotTitle,
      ctfScore,
      exportCsv,
      detailOpen,
      teamDetailOpen,
      teamDetailTeam,
      teamDetailTrendSeries,
      detailLoading,
      detailLoadingMore,
      detailError,
      detail,
      detailEntries,
      detailTeam,
      detailColumn,
      loadDetailPage,
      openDetail,
      openTeamDetail,
      showOlderRoundWindow,
      showNewerRoundWindow,
      showLatestRoundWindow,
      entryActor,
      adjustmentOpen,
      adjustmentLoading,
      adjustmentLoadingMore,
      adjustmentError,
      adjustmentDetail,
      adjustmentEntries,
      adjustmentTeam,
      loadAdjustmentPage,
      openAdjustments,
      adjustmentActor,
      adjustmentKind,
      ScoreboardSlotStatus
    }
}

export type CompetitionsByIdLeaderboardPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useCompetitionsByIdLeaderboardPage>>>
