import {
  getLeaderboardEndpoint,
  getScoreboardChallengeCatalogEndpoint,
  getScoreboardSchemaEndpoint,
} from '~/api'
import type {
  NoCtfapiEndpointsCompetitionsScoreboardChallengeCatalogResponse,
  NoCtfapiEndpointsCompetitionsScoreboardSchemaResponse,
  NoCtfapiEndpointsCompetitionsScoreboardSnapshotResponse,
} from '~/api'
import { createTrailingRefresh } from '~/lib/latest-page-refresh'

interface ScoreboardUpdatedPayload {
  competitionId: string | null
  version: number | null
  schemaRevision: number | null
  challengeCatalogRevision: number | null
}

function numberField(payload: unknown, key: string): number | null {
  if (!payload || typeof payload !== 'object') return null
  const value = Reflect.get(payload, key)
  return typeof value === 'number' && Number.isFinite(value) ? value : null
}

function scoreboardUpdatedPayload(payload: unknown): ScoreboardUpdatedPayload {
  return {
    competitionId: competitionHubString(payload, 'competitionId'),
    version: numberField(payload, 'version'),
    schemaRevision: numberField(payload, 'schemaRevision'),
    challengeCatalogRevision: numberField(payload, 'challengeCatalogRevision'),
  }
}

/**
 * Owns the three normalized scoreboard resources. Every accepted refresh is
 * generation-fenced, so a late response can never replace newer page state.
 */
export function useScoreboardMatrix(competitionId: string) {
  const catalog = ref<NoCtfapiEndpointsCompetitionsScoreboardChallengeCatalogResponse | null>(null)
  const schema = ref<NoCtfapiEndpointsCompetitionsScoreboardSchemaResponse | null>(null)
  const snapshot = ref<NoCtfapiEndpointsCompetitionsScoreboardSnapshotResponse | null>(null)
  const loading = ref(true)
  const refreshing = ref(false)
  const processing = ref(false)
  const error = ref<string | null>(null)
  let generation = 0
  let stopped = false
  let retryTimer: ReturnType<typeof setTimeout> | null = null

  function scheduleProcessingRetry(): void {
    if (retryTimer || stopped) return
    retryTimer = setTimeout(() => {
      retryTimer = null
      void refresh({ snapshot: true })
    }, 2000)
  }

  async function refresh(options: {
    catalog?: boolean
    schema?: boolean
    snapshot?: boolean
  } = { catalog: true, schema: true, snapshot: true }): Promise<void> {
    const requestGeneration = ++generation
    refreshing.value = true
    const wantCatalog = options.catalog ?? false
    const wantSchema = options.schema ?? false
    const wantSnapshot = options.snapshot ?? true
    const [catalogResult, schemaResult, snapshotResult] = await Promise.all([
      wantCatalog
        ? getScoreboardChallengeCatalogEndpoint({ path: { competitionId } })
        : Promise.resolve(null),
      wantSchema
        ? getScoreboardSchemaEndpoint({ path: { competitionId } })
        : Promise.resolve(null),
      wantSnapshot
        ? getLeaderboardEndpoint({ path: { competitionId } })
        : Promise.resolve(null),
    ])
    if (stopped || requestGeneration !== generation) return

    const failures = [catalogResult, schemaResult, snapshotResult]
      .filter(result => result?.error)
      .map(result => parseApiError(result?.error, translate('加载记分板失败')).message)
    if (failures.length) {
      error.value = failures[0] ?? translate('加载记分板失败')
    }
    else {
      if (catalogResult?.data) catalog.value = catalogResult.data
      if (schemaResult?.data) schema.value = schemaResult.data
      if (snapshotResult?.response?.status === 202) {
        processing.value = true
        scheduleProcessingRetry()
      }
      else if (snapshotResult?.data) {
        const incoming = snapshotResult.data as NoCtfapiEndpointsCompetitionsScoreboardSnapshotResponse
        if ((incoming.version ?? 0) >= (snapshot.value?.version ?? 0)) snapshot.value = incoming
        processing.value = false
      }
      error.value = null
    }
    loading.value = false
    refreshing.value = false
  }

  const trailingRefresh = createTrailingRefresh(async () => refresh({ snapshot: true }))
  let unwatch: (() => void) | null = null

  onMounted(() => {
    void refresh({ catalog: true, schema: true, snapshot: true })
    unwatch = watchCompetition(competitionId, {
      scoreboardUpdated: (raw) => {
        const notice = scoreboardUpdatedPayload(raw)
        const wantsCatalog = notice.challengeCatalogRevision !== null
          && notice.challengeCatalogRevision !== (catalog.value?.revision ?? null)
        const wantsSchema = notice.schemaRevision !== null
          && notice.schemaRevision !== (schema.value?.revision ?? null)
        if ((notice.version ?? 0) <= (snapshot.value?.version ?? 0)
          && !wantsCatalog && !wantsSchema) return
        if (wantsCatalog || wantsSchema) {
          void refresh({ catalog: wantsCatalog, schema: wantsSchema, snapshot: true })
          return
        }
        void trailingRefresh()
      },
      onReconnected: () => void refresh({ catalog: true, schema: true, snapshot: true }),
    })
  })

  onBeforeUnmount(() => {
    stopped = true
    generation += 1
    if (retryTimer) clearTimeout(retryTimer)
    retryTimer = null
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

  return {
    catalog,
    schema,
    snapshot,
    loading,
    refreshing,
    processing,
    error,
    refresh,
    challengesById,
    roundsById,
    columnsByIndex,
    actorsByIndex,
  }
}
