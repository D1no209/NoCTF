import { markRaw, toRefs } from 'vue'

import { RefreshCw } from '@lucide/vue'
import { listMyTeamRuntimes } from '../../api'
import type { NoCtfapiEndpointsRuntimeTeamRuntimeListItemResponse } from '../../api'
import { useOffsetPagination } from '../../composables/useOffsetPagination'
import { createTrailingRefresh } from '../../lib/latest-page-refresh'
import { competitionChallengePath } from '../../utils/app-routes'
import RuntimeCardComponent from '../challenges/RuntimeCard.vue'

type TeamRuntime = NoCtfapiEndpointsRuntimeTeamRuntimeListItemResponse

export function useTeamRuntimeManager(props: Readonly<{ competitionId: string }>) {
  const pagination = useOffsetPagination<TeamRuntime>(async ({ offset, limit, desc }) => {
    const { data, error } = await listMyTeamRuntimes({
      path: { competitionId: props.competitionId },
      query: { offset, limit, desc },
    })
    if (error || !data) throw error
    return { items: data.items ?? [], total: data.total ?? 0 }
  }, { initialPageSize: 10, initialDesc: true })

  const { items, loading, error, initialized, page, pageCount, total, limit: pageLimit } = pagination
  const selectedRuntimeId = ref<string | null>(null)
  const selected = computed(() => items.value.find(item => item.runtime?.id === selectedRuntimeId.value) ?? null)

  watch(items, rows => {
    if (!rows.some(item => item.runtime?.id === selectedRuntimeId.value))
      selectedRuntimeId.value = rows[0]?.runtime?.id ?? null
  }, { immediate: true })

  const refresh = createTrailingRefresh(() => pagination.loadPage(page.value))

  let unwatchCompetition: (() => void) | undefined
  onMounted(() => {
    void pagination.loadPage()
    unwatchCompetition = watchCompetition(props.competitionId, {
      competitionEventChanged: event => {
        if (event.kind === 'RuntimeCreated' || event.kind === 'RuntimeReset'
          || event.kind === 'RuntimeExtended' || event.kind === 'RuntimeStateChanged')
          void refresh()
      },
      onReconnected: () => { void refresh() },
    })
  })
  onUnmounted(() => unwatchCompetition?.())

  function selectRuntime(id: string): void {
    selectedRuntimeId.value = id
  }

  function challengePath(competitionChallengeId: string): string {
    return competitionChallengePath(props.competitionId, competitionChallengeId)
  }

  const RuntimeCard = markRaw(RuntimeCardComponent)

  return {
    ...toRefs(props),
    RefreshCw,
    RuntimeCard,
    items,
    loading,
    error,
    initialized,
    page,
    pageCount,
    total,
    pageLimit,
    selected,
    selectRuntime,
    challengePath,
    refresh,
    loadPage: pagination.loadPage,
    setPageSize: pagination.setPageSize,
  }
}

export type TeamRuntimeManagerViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useTeamRuntimeManager>>>
