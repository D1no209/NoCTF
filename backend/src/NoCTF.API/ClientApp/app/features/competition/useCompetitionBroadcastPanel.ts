import { message as describeMessage } from '../../utils/i18n'
import type { UiMessage } from '../../utils/i18n'
import { toRefs } from 'vue'

import { Megaphone } from '@lucide/vue'
import { listCompetitionEvents } from '../../api'
import type { NoCtfapiEndpointsCompetitionsEventsCompetitionEventResponse } from '../../api'
import { createTrailingRefresh } from '../../lib/latest-page-refresh'
import { motionAttributes } from '../../motion/presets'
import {
  competitionBroadcastIdentity, competitionBroadcastKinds, competitionBroadcastQueryWindow,
  isCompetitionBroadcastKind, mergeCompetitionBroadcasts, previousCompetitionBroadcastWindow,
} from '../../utils/competition-broadcast'
import type { CompetitionBroadcastQueryWindow } from '../../utils/competition-broadcast'

type CompetitionEvent = NoCtfapiEndpointsCompetitionsEventsCompetitionEventResponse
export const competitionBroadcastPageSize = 20
interface BroadcastPage {
  window: CompetitionBroadcastQueryWindow
  cursor: string | null
  startAt: number
}

/** Owns state, effects and commands for CompetitionBroadcastPanel. */
export function useCompetitionBroadcastPanel(props: Readonly<Omit<{
  competitionId: string
  fill?: boolean
}, "fill"> & Required<Pick<{
  competitionId: string
  fill?: boolean
}, "fill">>>) {
  const ctx = inject(competitionContextKey)!

  const items = ref<CompetitionEvent[]>([])

  const loading = ref(true)
  const loadingMore = ref(false)
  const hasMore = ref(false)
  const historyError = ref<UiMessage | null>(null)

  const error = ref<UiMessage | null>(null)

  const initialized = ref(false)

  const enteringIdentities = ref<ReadonlySet<string>>(new Set())

  let latestNotifiedAt = 0
  let generation = 0
  let disposed = false
  let feedStarted = false
  let historyPage: BroadcastPage | null = null
  const newerPages: BroadcastPage[] = []
  let clearEnteringTimer: ReturnType<typeof setTimeout> | undefined

  function nextPage(page: BroadcastPage, cursor?: string | null): BroadcastPage | null {
    if (cursor) return { ...page, cursor }
    const window = previousCompetitionBroadcastWindow(page.window, page.startAt)
    return window ? { ...page, window, cursor: null } : null
  }

  function updateHasMore(): void {
    hasMore.value = historyPage !== null || newerPages.length > 0
  }

  function reset(): void {
    generation += 1
    items.value = []
    loading.value = true
    loadingMore.value = false
    error.value = null
    historyError.value = null
    initialized.value = false
    feedStarted = false
    historyPage = null
    newerPages.length = 0
    enteringIdentities.value = new Set()
    updateHasMore()
  }

  async function loadMore(): Promise<void> {
    const page = newerPages[0] ?? historyPage
    if (!page || loadingMore.value || disposed) return
    const requestGeneration = generation
    const competitionId = props.competitionId
    loadingMore.value = true
    historyError.value = null
    try {
      const { data, error: requestError } = await listCompetitionEvents({
        path: { competitionId },
        query: { ...page.window, kinds: competitionBroadcastKinds,
          offset: 0, limit: competitionBroadcastPageSize, desc: true, cursor: page.cursor ?? undefined },
      })
      if (disposed || requestGeneration !== generation) return
      if (requestError || !data) throw requestError
      // Merge into the current list: a simultaneous live refresh may have prepended rows.
      items.value = mergeCompetitionBroadcasts(items.value, data.items ?? [])
      const remaining = nextPage(page, data.nextCursor)
      const index = newerPages.indexOf(page)
      if (index >= 0) newerPages.splice(index, 1, ...(remaining ? [remaining] : []))
      else if (historyPage === page) historyPage = remaining
      updateHasMore()
    }
    catch (requestError) {
      if (!disposed && requestGeneration === generation)
        historyError.value = parseApiError(requestError, describeMessage('competitions.competitionBroadcast.error.loadHistoryFailed')).displayMessage
    }
    finally {
      if (!disposed && requestGeneration === generation) loadingMore.value = false
    }
  }

  function markInsertions(
    previous: readonly CompetitionEvent[],
    next: readonly CompetitionEvent[],
  ): void {
    const existing = new Set(previous.map(competitionBroadcastIdentity))
    const inserted = next
      .map(competitionBroadcastIdentity)
      .filter(identity => !existing.has(identity))
    if (inserted.length === 0) return
    enteringIdentities.value = new Set([
      ...enteringIdentities.value,
      ...inserted,
    ])
    if (clearEnteringTimer) clearTimeout(clearEnteringTimer)
    clearEnteringTimer = setTimeout(() => {
      enteringIdentities.value = new Set()
      clearEnteringTimer = undefined
    }, 240)
  }

  function broadcastMotionAttributes(event: CompetitionEvent) {
    return enteringIdentities.value.has(competitionBroadcastIdentity(event))
      ? motionAttributes('list-enter')
      : undefined
  }

  async function load(): Promise<void> {
    if (disposed) return
    const competition = ctx.competition.value
    if (!competition) return
    const status = competition.status ?? null
    const startAt = competition.startTime ? Date.parse(competition.startTime) : Number.NaN
    const now = Date.now()
    if (!Number.isFinite(startAt) || now < startAt || status === 'Draft' || status === 'Visible' || status === 'Published') {
      reset()
      loading.value = false
      initialized.value = true
      return
    }
    const initialLoad = !initialized.value
    if (initialLoad) {
      loading.value = true
      error.value = null
    }
    const requestGeneration = generation
    const queryWindow = competitionBroadcastQueryWindow(
      startAt,
      now,
      latestNotifiedAt,
    )!
    const newestAt = Date.parse(items.value[0]?.occurredAt ?? '')
    if (feedStarted && Number.isFinite(newestAt))
      queryWindow.from = new Date(Math.max(Date.parse(queryWindow.from), newestAt)).toISOString()
    try {
      const { data, error: requestError } = await listCompetitionEvents({
        path: { competitionId: props.competitionId },
        query: { ...queryWindow, kinds: competitionBroadcastKinds,
          offset: 0, limit: competitionBroadcastPageSize, desc: true },
      })
      if (disposed || requestGeneration !== generation) return
      if (requestError || !data) throw requestError
      const incoming = data.items ?? []
      const merged = mergeCompetitionBroadcasts(items.value, incoming)
      if (!feedStarted) {
        historyPage = nextPage({ window: queryWindow, cursor: null, startAt }, data.nextCursor)
        feedStarted = true
      }
      else {
        // A reconnect can miss more than one batch. Keep its own signed cursor
        // and fixed window so those events remain reachable without bulk fetching.
        const unseen = incoming.some(event => !items.value.some(current =>
          competitionBroadcastIdentity(current) === competitionBroadcastIdentity(event)))
        if (unseen || newerPages.length === 0) {
          const remaining = nextPage({ window: queryWindow, cursor: null,
            startAt: Number.isFinite(newestAt) ? newestAt : startAt }, data.nextCursor)
          if (remaining) newerPages.unshift(remaining)
        }
      }
      if (!initialLoad) markInsertions(items.value, merged)
      items.value = merged
      initialized.value = true
      error.value = null
      updateHasMore()
    }
    catch (requestError) {
      if (!disposed && requestGeneration === generation && initialLoad)
        error.value = parseApiError(requestError, describeMessage('competitions.competitionBroadcast.error.loadEventReportFailed')).displayMessage
    }
    finally {
      if (!disposed && requestGeneration === generation) loading.value = false
    }
  }

  const refreshLatest = createTrailingRefresh(load)

  watch(
    () => ctx.competition.value,
    competition => {
      if (competition) void refreshLatest()
    },
    { immediate: true },
  )

  let unwatch: (() => void) | undefined
  let mounted = false

  function subscribe(): void {
    unwatch?.()
    unwatch = watchCompetition(props.competitionId, {
      competitionEventChanged: notification => {
        if (!isCompetitionBroadcastKind(notification.kind)) return
        const notifiedAt = Date.parse(notification.occurredAt)
        if (Number.isFinite(notifiedAt)) latestNotifiedAt = Math.max(latestNotifiedAt, notifiedAt)
        void refreshLatest()
      },
      onReconnected: () => void refreshLatest(),
    })
  }

  watch(() => props.competitionId, () => {
    latestNotifiedAt = 0
    reset()
    if (mounted) subscribe()
    void refreshLatest()
  })

  onMounted(() => {
    mounted = true
    subscribe()
  })

  onUnmounted(() => {
    disposed = true
    generation += 1
    unwatch?.()
    if (clearEnteringTimer) clearTimeout(clearEnteringTimer)
  })

  return {
      ...toRefs(props),
      Megaphone,
      items,
      loading,
      loadingMore,
      hasMore,
      historyError,
      error,
      emptyDescriptionKey: computed(() => ctx.competition.value?.mode === 'LiveSolo'
        ? 'competitions.competitionBroadcast.description.liveSolo' as const
        : 'competitions.competitionBroadcast.description.bloodListQuestionsDiscipline' as const),
      broadcastMotionAttributes,
      refreshLatest,
      loadMore,
    }
}

export type CompetitionBroadcastPanelViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useCompetitionBroadcastPanel>>>
