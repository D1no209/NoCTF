import { message as describeMessage } from '../utils/i18n'
import type { UiMessage } from '../utils/i18n'
import {
  getLeaderboardEndpoint,
  getScoreboardChallengeCatalogEndpoint,
  getScoreboardSchemaEndpoint,
} from '../api'
import type {
  NoCtfapiEndpointsCompetitionsScoreboardChallengeCatalogResponse,
  NoCtfapiEndpointsCompetitionsScoreboardSchemaResponse,
  NoCtfapiEndpointsCompetitionsScoreboardSnapshotResponse,
} from '../api'
import type { ScoreboardUpdatedNotification } from './useCompetitionHub'
import { createTrailingRefresh } from '../lib/latest-page-refresh'
import {
  isScoreboardVersionAtLeast,
  isCoherentScoreboardBundle,
  newestScoreboardVersion,
  needsAwdpRoundRefresh,
  shouldRestoreRequestedRoundWindow,
} from '../utils/scoreboard-coherence'
import type { ScoreboardRefreshOutcome } from '../utils/scoreboard-coherence'

interface ScoreboardUpdatedPayload {
  competitionId: string | null
  version: string | null
  schemaRevision: string | null
  challengeCatalogRevision: string | null
}

interface ScoreboardRefreshOptions {
  catalog?: boolean
  schema?: boolean
  snapshot?: boolean
}

function stringField(payload: unknown, key: string): string | null {
  if (!payload || typeof payload !== 'object') return null
  const value = Reflect.get(payload, key)
  return typeof value === 'string' && value.length > 0 ? value : null
}

function scoreboardUpdatedPayload(payload: ScoreboardUpdatedNotification): ScoreboardUpdatedPayload {
  return {
    competitionId: competitionHubString(payload, 'competitionId'),
    version: stringField(payload, 'version'),
    schemaRevision: stringField(payload, 'schemaRevision'),
    challengeCatalogRevision: stringField(payload, 'challengeCatalogRevision'),
  }
}

/**
 * Owns the three normalized scoreboard resources. Every accepted refresh is
 * generation- and version-fenced, so an older response cannot replace newer page state.
 */
export function useScoreboardMatrix(competitionId: string, options: { pollRounds?: boolean } = {}) {
  const catalog = ref<NoCtfapiEndpointsCompetitionsScoreboardChallengeCatalogResponse | null>(null)
  const schema = ref<NoCtfapiEndpointsCompetitionsScoreboardSchemaResponse | null>(null)
  const snapshot = ref<NoCtfapiEndpointsCompetitionsScoreboardSnapshotResponse | null>(null)
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
  let minimumSnapshotVersion: string | null = null

  function scheduleProcessingRetry(): void {
    if (retryTimer || stopped) return
    retryTimer = setTimeout(() => {
      retryTimer = null
      void refresh({ catalog: true, schema: true, snapshot: true })
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
      void refresh({ catalog: true, schema: true, snapshot: true })
    }, 250)
  }

  async function refresh(
    options: ScoreboardRefreshOptions = { catalog: true, schema: true, snapshot: true },
  ): Promise<ScoreboardRefreshOutcome> {
    const requestGeneration = ++generation
    const endingRound = requestedEndingRound.value
    refreshing.value = true
    const wantCatalog = options.catalog ?? false
    const wantSchema = options.schema ?? false
    const wantSnapshot = options.snapshot ?? true
    const [catalogResult, schemaResult, snapshotResult] = await Promise.all([
      wantCatalog
        ? getScoreboardChallengeCatalogEndpoint({ path: { competitionId } })
        : Promise.resolve(null),
      wantSchema
        ? getScoreboardSchemaEndpoint({
            path: { competitionId },
            query: { endingRound },
          })
        : Promise.resolve(null),
      wantSnapshot
        ? getLeaderboardEndpoint({
            path: { competitionId },
            query: { endingRound },
          })
        : Promise.resolve(null),
    ])
    if (stopped || requestGeneration !== generation) return 'superseded'

    const failures = [catalogResult, schemaResult, snapshotResult]
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
        const candidateSchema = schemaResult?.data ?? schema.value
        let candidateSnapshot = snapshot.value
        if (snapshotResult?.data) {
          const incoming = snapshotResult.data as NoCtfapiEndpointsCompetitionsScoreboardSnapshotResponse
          candidateSnapshot = incoming
        }
        const sameDataScope = candidateSnapshot?.dataScope === snapshot.value?.dataScope
        let versionFloor = sameDataScope ? (snapshot.value?.version ?? null) : null
        if (candidateSnapshot?.dataScope === 'Live')
          versionFloor = newestScoreboardVersion(versionFloor, minimumSnapshotVersion)
        if (isCoherentScoreboardBundle(candidateCatalog, candidateSchema, candidateSnapshot)
          && isScoreboardVersionAtLeast(candidateSnapshot?.version, versionFloor)) {
          catalog.value = candidateCatalog
          schema.value = candidateSchema
          snapshot.value = candidateSnapshot
          minimumSnapshotVersion = candidateSnapshot?.dataScope === 'Live'
            ? newestScoreboardVersion(minimumSnapshotVersion, candidateSnapshot.version)
            : null
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
    const outcome = await refresh({ schema: true, snapshot: true })
    if (shouldRestoreRequestedRoundWindow(outcome))
      requestedEndingRound.value = previousEndingRound
  }

  let pendingRefresh: Required<ScoreboardRefreshOptions> = {
    catalog: false,
    schema: false,
    snapshot: false,
  }
  const trailingRefresh = createTrailingRefresh(async () => {
    const requested = pendingRefresh
    pendingRefresh = { catalog: false, schema: false, snapshot: false }
    await refresh(requested)
  })
  function queueRefresh(options: ScoreboardRefreshOptions): Promise<void> {
    pendingRefresh = {
      catalog: pendingRefresh.catalog || (options.catalog ?? false),
      schema: pendingRefresh.schema || (options.schema ?? false),
      snapshot: pendingRefresh.snapshot || (options.snapshot ?? true),
    }
    return trailingRefresh()
  }
  let unwatch: (() => void) | null = null

  onMounted(() => {
    void refresh({ catalog: true, schema: true, snapshot: true })
    if (options.pollRounds !== false) {
      roundRefreshTimer = setInterval(() => {
        if (!refreshing.value && needsAwdpRoundRefresh(schema.value, snapshot.value))
          void queueRefresh({ catalog: true, schema: true, snapshot: true })
      }, 10_000)
    }
    unwatch = watchCompetition(competitionId, {
      competitionLifecycleChanged: () => void queueRefresh({ catalog: true, schema: true, snapshot: true }),
      scoreboardUpdated: (raw) => {
        const notice = scoreboardUpdatedPayload(raw)
        if (snapshot.value === null || snapshot.value.dataScope === 'Live')
          minimumSnapshotVersion = newestScoreboardVersion(minimumSnapshotVersion, notice.version)
        const wantsCatalog = notice.challengeCatalogRevision !== null
          && notice.challengeCatalogRevision !== (catalog.value?.revision ?? null)
        const wantsSchema = notice.schemaRevision !== null
          && notice.schemaRevision !== (schema.value?.revision ?? null)
        const wantsSnapshot = notice.version !== null
          && !isScoreboardVersionAtLeast(snapshot.value?.version, notice.version)
        if (!wantsSnapshot && !wantsCatalog && !wantsSchema) return
        void queueRefresh({
          catalog: wantsCatalog,
          schema: wantsSchema,
          snapshot: true,
        })
      },
      onReconnected: () => void queueRefresh({ catalog: true, schema: true, snapshot: true }),
    })
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
    (schema.value?.rounds ?? [])
      .filter(round => round.id)
      .map(round => [round.id!, round]),
  ))
  const columnsByIndex = computed(() => new Map(
    (schema.value?.columns ?? [])
      .filter(column => column.index !== undefined)
      .map(column => [column.index!, column]),
  ))
  const actorsByIndex = computed(() => new Map(
    (snapshot.value?.actors ?? [])
      .filter(actor => actor.index !== undefined)
      .map(actor => [actor.index!, actor]),
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
    columnsByIndex,
    actorsByIndex,
  }
}
