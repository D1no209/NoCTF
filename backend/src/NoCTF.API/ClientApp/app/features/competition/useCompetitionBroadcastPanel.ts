import { dateObject } from '../../utils/date-value'
import { dateTimestamp } from '../../utils/date-value'

import { api } from '../../lib/api'
import { message as describeMessage } from '../../utils/i18n'
import type { UiMessage } from '../../utils/i18n'
import { toRefs } from 'vue'

import { Megaphone } from '@lucide/vue'

import type { NoCTFAPIEndpointsCompetitionsEventsCompetitionEventResponse } from '../../api/models'
import { createTrailingRefresh } from '../../lib/latest-page-refresh'
import { motionAttributes } from '../../motion/presets'

type CompetitionEvent = NoCTFAPIEndpointsCompetitionsEventsCompetitionEventResponse

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

  const error = ref<UiMessage | null>(null)

  const initialized = ref(false)

  const enteringIdentities = ref<ReadonlySet<string>>(new Set())

  let latestNotifiedAt = 0
  let clearEnteringTimer: ReturnType<typeof setTimeout> | undefined

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
    const competition = ctx.competition.value
    if (!competition) return
    const status = competition.status ?? null
    const startAt = competition.startTime ? dateTimestamp(competition.startTime) : Number.NaN
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
    let requestError: unknown;
    const data = await api.api.v1.competitions.byCompetitionId(props.competitionId).events.get({ queryParameters: {
        from: dateObject(queryWindow.from ?? undefined),
        to: dateObject(queryWindow.to ?? undefined),
        kinds: competitionBroadcastKinds,
        offset: 0,
        limit: 10,
        desc: true,
      } }).catch(cause => { requestError = cause; return undefined });
    if (requestError || !data) {
      if (initialLoad)
        error.value = parseApiError(requestError, describeMessage("competitions.competitionBroadcast.error.loadEventReportFailed")).displayMessage
      loading.value = false
      return
    }
    const merged = mergeCompetitionBroadcasts(items.value, data.items ?? [])
    if (!initialLoad) markInsertions(items.value, merged)
    items.value = merged
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
        const notifiedAt = dateTimestamp(notification.occurredAt)
        if (Number.isFinite(notifiedAt)) latestNotifiedAt = Math.max(latestNotifiedAt, notifiedAt)
        void refreshLatest()
      },
      onReconnected: () => void refreshLatest(),
    })
  })

  onUnmounted(() => {
    unwatch?.()
    if (clearEnteringTimer) clearTimeout(clearEnteringTimer)
  })

  return {
      ...toRefs(props),
      Megaphone,
      items,
      loading,
      error,
      broadcastMotionAttributes,
      refreshLatest
    }
}

export type CompetitionBroadcastPanelViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useCompetitionBroadcastPanel>>>
