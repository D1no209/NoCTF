import { toRefs } from 'vue'

import { Megaphone } from '@lucide/vue'
import { listCompetitionEvents } from '../../api'
import type { NoCtfapiEndpointsCompetitionsEventsCompetitionEventResponse } from '../../api'
import { createTrailingRefresh } from '../../lib/latest-page-refresh'

type CompetitionEvent = NoCtfapiEndpointsCompetitionsEventsCompetitionEventResponse

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

  const error = ref<string | null>(null)

  const initialized = ref(false)

  let latestNotifiedAt = 0

  async function load(): Promise<void> {
    const competition = ctx.competition.value
    if (!competition) return
    const status = competition.status ?? null
    const startAt = competition.startTime ? Date.parse(competition.startTime) : Number.NaN
    const now = Date.now()
    if (!Number.isFinite(startAt) || now < startAt || status === 'Draft' || status === 'Visible' || status === 'Published') {
      items.value = []
      error.value = null
      loading.value = false
      initialized.value = true
      return
    }
    const initialLoad = !initialized.value
    if (initialLoad) {
      loading.value = true
      error.value = null
    }
    const queryWindow = competitionBroadcastQueryWindow(
      startAt,
      now,
      latestNotifiedAt,
    )!
    const { data, error: requestError } = await listCompetitionEvents({
      path: { competitionId: props.competitionId },
      query: {
        from: queryWindow.from,
        to: queryWindow.to,
        kinds: competitionBroadcastKinds,
        limit: 10,
      },
    })
    if (requestError || !data) {
      if (initialLoad)
        error.value = parseApiError(requestError, translate("ui.failedToLoadEventReport")).message
      loading.value = false
      return
    }
    items.value = mergeCompetitionBroadcasts(items.value, data.items ?? [])
    initialized.value = true
    loading.value = false
    error.value = null
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

  onMounted(() => {
    unwatch = watchCompetition(props.competitionId, {
      competitionEventChanged: notification => {
        if (!isCompetitionBroadcastKind(notification.kind)) return
        const notifiedAt = Date.parse(notification.occurredAt)
        if (Number.isFinite(notifiedAt)) latestNotifiedAt = Math.max(latestNotifiedAt, notifiedAt)
        void refreshLatest()
      },
      onReconnected: () => void refreshLatest(),
    })
  })

  onUnmounted(() => unwatch?.())

  return {
      ...toRefs(props),
      Megaphone,
      items,
      loading,
      error,
      refreshLatest
    }
}

export type CompetitionBroadcastPanelViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useCompetitionBroadcastPanel>>>
