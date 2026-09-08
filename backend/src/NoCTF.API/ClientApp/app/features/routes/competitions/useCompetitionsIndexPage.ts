import { markRaw } from 'vue'

import { listCompetitionsEndpoint } from '../../../api'
import type { NoCtfapiEndpointsCompetitionsCompetitionResponse } from '../../../api'
import CompetitionCardComponent from '../../competitions/CompetitionCard.vue'

type Competition = NoCtfapiEndpointsCompetitionsCompetitionResponse

/** Owns state, effects and commands for CompetitionsIndexPage. */
export function useCompetitionsIndexPage() {
  const items = ref<Competition[]>([])

  const loading = ref(true)

  const error = ref<string | null>(null)

  onMounted(async () => {
    const { data, error: err } = await listCompetitionsEndpoint()
    loading.value = false
    if (err || !data) {
      error.value = parseApiError(err, translate("ui.failedToLoadContestList")).message
      return
    }
    items.value = data.items ?? []
  })

  const running = computed(() =>
    items.value
      .filter((c) => c.status === 'Running' || c.status === 'Paused')
      .sort((a, b) => (a.endTime ?? '').localeCompare(b.endTime ?? '')),
  )

  const upcoming = computed(() =>
    items.value
      .filter((c) => c.status === 'Published' || c.status === 'Visible')
      .sort((a, b) => (a.startTime ?? '').localeCompare(b.startTime ?? '')),
  )

  const finished = computed(() =>
    items.value
      .filter((c) => c.status === 'Finished')
      .sort((a, b) => (b.endTime ?? '').localeCompare(a.endTime ?? '')),
  )

  const tab = ref('running')

  const CompetitionCard = markRaw(CompetitionCardComponent)

  return {
      loading,
      error,
      running,
      upcoming,
      finished,
      tab,
      CompetitionCard
    }
}

export type CompetitionsIndexPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useCompetitionsIndexPage>>>
