import { onMounted, reactive, watch } from 'vue'
import { useRouter } from 'vue-router'
import type { NoCtfapiEndpointsCompetitionsCompetitionListItemDto } from '@/api/generated/types.gen'
import { competitionApi } from '@/api/noctf'

export type CompetitionListItemDto = NoCtfapiEndpointsCompetitionsCompetitionListItemDto
export type CompetitionsLoadState = 'loading' | 'error' | 'empty' | 'filtered-empty' | 'ready'

// V1's imperative list state machine (manual fetch, not vue-query) is the
// canonical behavior for this page — see the health page precedent.
export function useCompetitionsPage() {
  const router = useRouter()

  const state = reactive({
    search: '',
    statusFilter: 'all',
    modeFilter: 'all',
    competitions: [] as CompetitionListItemDto[],
    visibleCompetitions: [] as CompetitionListItemDto[],
    loadState: 'loading' as CompetitionsLoadState,
  })

  function resetFilters() {
    state.search = ''
    state.statusFilter = 'all'
    state.modeFilter = 'all'
  }

  function updateListState() {
    const query = state.search.trim().toLowerCase()
    state.visibleCompetitions = state.competitions.filter((competition) => {
      const matchesSearch = !query
        || (competition.title ?? '').toLowerCase().includes(query)
        || (competition.description ?? '').toLowerCase().includes(query)
      const matchesStatus = state.statusFilter === 'all'
        || (competition.status ?? '').toLowerCase() === state.statusFilter
      const matchesMode = state.modeFilter === 'all'
        || (competition.gameModeType ?? '').toLowerCase() === state.modeFilter
      return matchesSearch && matchesStatus && matchesMode
    })

    if (state.competitions.length === 0)
      state.loadState = 'empty'
    else if (state.visibleCompetitions.length === 0)
      state.loadState = 'filtered-empty'
    else
      state.loadState = 'ready'
  }

  async function loadCompetitions() {
    state.loadState = 'loading'

    try {
      const response = await competitionApi.list<CompetitionListItemDto[]>()
      state.competitions = Array.isArray(response) ? response : []
      updateListState()
    }
    catch {
      state.competitions = []
      state.visibleCompetitions = []
      state.loadState = 'error'
    }
  }

  watch(() => [state.search, state.statusFilter, state.modeFilter], () => {
    if (state.loadState !== 'loading' && state.loadState !== 'error')
      updateListState()
  })

  onMounted(() => {
    void loadCompetitions()
  })

  function goCompetitionDetail(id: string) {
    void router.push({ name: 'competition-detail', params: { id } })
  }

  function goCompetitionRegister(id: string) {
    void router.push({ name: 'competition-register', params: { id } })
  }

  return {
    state,
    resetFilters,
    loadCompetitions,
    goCompetitionDetail,
    goCompetitionRegister,
  }
}
