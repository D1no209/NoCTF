import { message as describeMessage } from '../utils/i18n'
import type { InjectionKey, Ref } from 'vue'
import { hasInjectionContext, inject, provide } from 'vue'
import type { UiMessage } from '../utils/i18n'
import {
  getLeaderboardEndpoint,
  getScoreboardChallengeCatalogEndpoint,
} from '../api'
import type { NoCtfapiEndpointsCompetitionsScoreboardChallengeCatalogResponse } from '../api'
import { normalizeScoreboardSnapshot } from '../utils/scoreboard-board'
import type { ScoreboardLayout, ScoreboardSnapshot } from '../utils/scoreboard-board'
import type { ScoreboardUpdatedNotification } from './useCompetitionHub'
import { createTrailingRefresh } from '../lib/latest-page-refresh'
import {
  isScoreboardVersionAtLeast,
  isCoherentScoreboardBundle,
  canReplaceScoreboardSnapshot,
  needsAwdpRoundRefresh,
  shouldRestoreRequestedRoundWindow,
} from '../utils/scoreboard-coherence'
import type { ScoreboardRefreshOutcome } from '../utils/scoreboard-coherence'

interface ScoreboardUpdatedPayload {
  competitionId: string | null
  version: string | null
  challengeCatalogRevision: string | null
}

/** The leaderboard response already contains its columns and rounds; titles come from the catalog. */
interface ScoreboardRefreshOptions {
  catalog?: boolean
  snapshot?: boolean
}

function stringField(payload: unknown, key: string): string | null {
  if (!payload || typeof payload !== 'object') return null
  const value = (payload as Record<string, unknown>)[key]
  return typeof value === 'string' && value.length > 0 ? value : null
}

function scoreboardUpdatedPayload(payload: ScoreboardUpdatedNotification): ScoreboardUpdatedPayload {
  return {
    competitionId: competitionHubString(payload, 'competitionId'),
    version: stringField(payload, 'version'),
    challengeCatalogRevision: stringField(payload, 'challengeCatalogRevision'),
  }
}

/**
 * Owns the catalog and the self-describing leaderboard; the column layout is derived from it. Every accepted refresh is
 * generation- and version-fenced, so an older response cannot replace newer page state.
 */
type ScoreboardMatrix = ReturnType<typeof createScoreboardMatrix>
const sharedScoreboardKey: InjectionKey<{ competitionId: string; board: ScoreboardMatrix }> = Symbol('competition-scoreboard')

export function useScoreboardMatrix(competitionId: string, options: { pollRounds?: boolean } = {}) {
  const shared = hasInjectionContext() ? inject(sharedScoreboardKey, null) : null
  if (shared?.competitionId === competitionId && options.pollRounds !== false) return shared.board
  return createScoreboardMatrix(competitionId, options)
}

export function provideCompetitionScoreboard(competitionId: string, enabled: Readonly<Ref<boolean>>) {
  const board = createScoreboardMatrix(competitionId, { enabled })
  if (hasInjectionContext()) provide(sharedScoreboardKey, { competitionId, board })
  return board
}

function createScoreboardMatrix(competitionId: string, options: { pollRounds?: boolean; enabled?: Readonly<Ref<boolean>> } = {}) {
  const catalog = ref<NoCtfapiEndpointsCompetitionsScoreboardChallengeCatalogResponse | null>(null)
  const snapshot = shallowRef<ScoreboardSnapshot | null>(null)
  const schema = computed<ScoreboardLayout | null>(() => snapshot.value?.layout ?? null)
  const loading = ref(true)
  const refreshing = ref(false)
  const processing = ref(false)
  const error = ref<UiMessage | null>(null)
  const requestedEndingRound = ref<number | null>(null)
  let generation = 0
  let stopped = false
  let retryTimer: ReturnType<typeof setTimeout> | null = null
  let coherenceRetryTimer: ReturnType<typeof setTimeout> | null = null
  let roundRefreshTimer: ReturnType<typeof setInterval> | null = null
  let coherenceRetryCount = 0

  function scheduleProcessingRetry(): void {
    if (retryTimer || stopped) return
    retryTimer = setTimeout(() => {
      retryTimer = null
      void refresh({ catalog: true, snapshot: true })
    }, 2000)
  }

  function scheduleCoherenceRetry(): void {
    if (coherenceRetryTimer || stopped) return
    if (coherenceRetryCount >= 3) {
      error.value = describeMessage("leaderboard.scoreboardMatrix.description.scoreboardDataRevisionsSynchronized")
      return
    }
    coherenceRetryCount += 1
    coherenceRetryTimer = setTimeout(() => {
      coherenceRetryTimer = null
      void refresh({ catalog: true, snapshot: true })
    }, 250)
  }

  async function load(
    options: ScoreboardRefreshOptions = { catalog: true, snapshot: true },
  ): Promise<ScoreboardRefreshOutcome> {
    const requestGeneration = ++generation
    const endingRound = requestedEndingRound.value
    refreshing.value = true
    const wantCatalog = options.catalog ?? false
    const wantSnapshot = options.snapshot ?? true
    const [catalogResult, snapshotResult] = await Promise.all([
      wantCatalog
        ? getScoreboardChallengeCatalogEndpoint({ path: { competitionId } })
        : Promise.resolve(null),
      wantSnapshot
        ? getLeaderboardEndpoint({
            path: { competitionId },
            query: { endingRound },
          })
        : Promise.resolve(null),
    ])
    if (stopped || requestGeneration !== generation) return 'superseded'

    const failures = [catalogResult, snapshotResult]
      .filter(result => result?.error)
      .map(result => parseApiError(result?.error, describeMessage("common.error.loadScoreboardFailed")).displayMessage)
    let outcome: ScoreboardRefreshOutcome = 'failed'
    if (failures.length) {
      error.value = failures[0] ?? translate("common.error.loadScoreboardFailed")
    }
    else {
      if (snapshotResult?.response?.status === 202) {
        processing.value = true
        error.value = null
        scheduleProcessingRetry()
        outcome = 'retrying'
      }
      else {
        const candidateCatalog = catalogResult?.data ?? catalog.value
        const candidateSnapshot = snapshotResult?.data
          ? normalizeScoreboardSnapshot(snapshotResult.data)
          : snapshot.value
        if (isCoherentScoreboardBundle(candidateCatalog, candidateSnapshot)
          && canReplaceScoreboardSnapshot(candidateSnapshot, snapshot.value)) {
          catalog.value = candidateCatalog
          snapshot.value = candidateSnapshot
          coherenceRetryCount = 0
          error.value = null
          outcome = 'accepted'
        }
        else {
          scheduleCoherenceRetry()
          outcome = 'retrying'
        }
        processing.value = false
      }
    }
    loading.value = false
    refreshing.value = false
    return outcome
  }

  async function selectRoundWindow(endingRound: number | null): Promise<void> {
    if (refreshing.value || requestedEndingRound.value === endingRound) return
    const previousEndingRound = requestedEndingRound.value
    requestedEndingRound.value = endingRound
    const outcome = await refresh({ snapshot: true })
    if (shouldRestoreRequestedRoundWindow(outcome))
      requestedEndingRound.value = previousEndingRound
  }

  let pendingRefresh: Required<ScoreboardRefreshOptions> = {
    catalog: false,
    snapshot: false,
  }
  let activeRead: Promise<ScoreboardRefreshOutcome> | null = null
  function refresh(options: ScoreboardRefreshOptions = { catalog: true, snapshot: true }): Promise<ScoreboardRefreshOutcome> {
    pendingRefresh = { catalog: pendingRefresh.catalog || (options.catalog ?? false),
      snapshot: pendingRefresh.snapshot || (options.snapshot ?? true) }
    if (activeRead) return activeRead
    activeRead = (async () => {
      let outcome: ScoreboardRefreshOutcome
      do {
        const requested = pendingRefresh
        pendingRefresh = { catalog: false, snapshot: false }
        outcome = await load(requested)
      } while (!stopped && (pendingRefresh.catalog || pendingRefresh.snapshot))
      return outcome
    })().finally(() => { activeRead = null })
    return activeRead
  }
  let targetNotice: ScoreboardUpdatedPayload | null = null
  function noticeRefreshOptions(): Required<ScoreboardRefreshOptions> {
    if (!targetNotice || targetNotice.version !== null
      && isScoreboardVersionAtLeast(snapshot.value?.version, targetNotice.version)
      && isCoherentScoreboardBundle(catalog.value, snapshot.value))
      return { catalog: false, snapshot: false }
    return {
      catalog: targetNotice.challengeCatalogRevision !== null
        && targetNotice.challengeCatalogRevision !== (catalog.value?.revision ?? null),
      snapshot: targetNotice.version !== null
        && !isScoreboardVersionAtLeast(snapshot.value?.version, targetNotice.version),
    }
  }
  const trailingRefresh = createTrailingRefresh(async () => {
    if (activeRead) await activeRead
    const requested = pendingRefresh
    pendingRefresh = { catalog: false, snapshot: false }
    const notice = noticeRefreshOptions()
    const needed = { catalog: requested.catalog || notice.catalog,
      snapshot: requested.snapshot || notice.snapshot }
    if (needed.catalog || needed.snapshot) await refresh(needed)
  })
  function queueRefresh(options: ScoreboardRefreshOptions): Promise<void> {
    pendingRefresh = {
      catalog: pendingRefresh.catalog || (options.catalog ?? false),
      snapshot: pendingRefresh.snapshot || (options.snapshot ?? true),
    }
    return trailingRefresh()
  }
  let unwatch: (() => void) | null = null
  const refreshNotified = createTrailingRefresh(async () => {
    if (!stopped && options.enabled?.value !== false)
      await queueRefresh({ catalog: false, snapshot: false })
  }, 500)

  function start() {
    if (stopped || unwatch || options.enabled?.value === false) return
    void refresh({ catalog: catalog.value === null, snapshot: true })
    if (options.pollRounds !== false) {
      roundRefreshTimer = setInterval(() => {
        if (!refreshing.value && needsAwdpRoundRefresh(snapshot.value))
          void queueRefresh({ snapshot: true })
      }, 10_000)
    }
    unwatch = watchCompetition(competitionId, {
      competitionLifecycleChanged: () => void queueRefresh({ catalog: true, snapshot: true }),
      scoreboardUpdated: (raw) => {
        const notice = scoreboardUpdatedPayload(raw)
        const wantsCatalog = notice.challengeCatalogRevision !== null
          && notice.challengeCatalogRevision !== (catalog.value?.revision ?? null)
        const wantsSnapshot = notice.version !== null
          && !isScoreboardVersionAtLeast(snapshot.value?.version, notice.version)
        if (!wantsSnapshot && !wantsCatalog) return
        if (targetNotice?.version && notice.version
          && !isScoreboardVersionAtLeast(notice.version, targetNotice.version)) return
        targetNotice = notice
        void refreshNotified()
      },
      onReconnected: () => void queueRefresh({ catalog: true, snapshot: true }),
    })
  }
  onMounted(start)
  watch(() => options.enabled?.value, value => {
    if (value !== false) { start(); return }
    generation++
    unwatch?.(); unwatch = null
    if (roundRefreshTimer) clearInterval(roundRefreshTimer)
    roundRefreshTimer = null
    if (retryTimer) clearTimeout(retryTimer)
    retryTimer = null
    if (coherenceRetryTimer) clearTimeout(coherenceRetryTimer)
    coherenceRetryTimer = null
  })
  const { user } = useAuth()
  watch(() => user.value?.userId, () => {
    targetNotice = null
    generation++; catalog.value = null; snapshot.value = null
    if (options.enabled?.value !== false) void queueRefresh({ catalog: true, snapshot: true })
  })

  onBeforeUnmount(() => {
    stopped = true
    generation += 1
    if (retryTimer) clearTimeout(retryTimer)
    retryTimer = null
    if (coherenceRetryTimer) clearTimeout(coherenceRetryTimer)
    coherenceRetryTimer = null
    if (roundRefreshTimer) clearInterval(roundRefreshTimer)
    roundRefreshTimer = null
    unwatch?.()
    unwatch = null
  })

  const challengesById = computed(() => new Map(
    (catalog.value?.items ?? [])
      .filter(item => item.id)
      .map(item => [item.id!, item]),
  ))
  const roundsById = computed(() => new Map(
    (schema.value?.rounds ?? []).map(round => [round.id, round]),
  ))
  const viewingLatestRounds = computed(() => {
    if (schema.value?.mode !== 'Awdp' && schema.value?.mode !== 'Awd') return true
    const windowEnd = schema.value.roundWindowEnd
    const latestRound = schema.value.latestRound
    return windowEnd === null || windowEnd === undefined
      || latestRound === null || latestRound === undefined
      || windowEnd >= latestRound
  })
  const canShowOlderRounds = computed(() => (schema.value?.mode === 'Awdp' || schema.value?.mode === 'Awd')
    && snapshot.value?.dataScope !== 'Frozen'
    && (schema.value.roundWindowStart ?? 1) > 1)
  const canShowNewerRounds = computed(() => (schema.value?.mode === 'Awdp' || schema.value?.mode === 'Awd')
    && snapshot.value?.dataScope !== 'Frozen'
    && !viewingLatestRounds.value)
  const detailEndingRound = computed(() => viewingLatestRounds.value
    ? null
    : schema.value?.roundWindowEnd ?? null)

  async function showOlderRounds(): Promise<void> {
    const windowStart = schema.value?.roundWindowStart
    if (!windowStart || windowStart <= 1) return
    await selectRoundWindow(windowStart - 1)
  }

  async function showNewerRounds(): Promise<void> {
    const windowEnd = schema.value?.roundWindowEnd
    const latestRound = schema.value?.latestRound
    if (!windowEnd || !latestRound || windowEnd >= latestRound) return
    const nextEnd = Math.min(latestRound, windowEnd + 50)
    await selectRoundWindow(nextEnd >= latestRound ? null : nextEnd)
  }

  async function showLatestRounds(): Promise<void> {
    await selectRoundWindow(null)
  }

  return {
    catalog,
    schema,
    snapshot,
    loading,
    refreshing,
    processing,
    error,
    requestedEndingRound,
    refresh,
    showOlderRounds,
    showNewerRounds,
    showLatestRounds,
    viewingLatestRounds,
    canShowOlderRounds,
    canShowNewerRounds,
    detailEndingRound,
    challengesById,
    roundsById,
  }
}
