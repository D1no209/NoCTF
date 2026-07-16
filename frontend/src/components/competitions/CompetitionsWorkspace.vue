<script setup lang="ts">
import { onMounted, reactive, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { competitionApi } from '@/api/noctf'
import AppLayout from '@/components/layout/AppLayout.vue'
import PageHeader from '@/components/layout/PageHeader.vue'
import CompetitionsExplorer from '@/components/competitions/CompetitionsExplorer.vue'

const { t } = useI18n()

interface Competition {
  id: string
  title: string
  description?: string | null
  status: string
  gameModeType?: string | null
  startTime: string
  endTime: string
  registeredTeamCount?: number | null
}

type LoadState = 'loading' | 'error' | 'empty' | 'filtered-empty' | 'ready'

const state = reactive({
  search: '',
  statusFilter: 'all',
  modeFilter: 'all',
  competitions: [] as Competition[],
  visibleCompetitions: [] as Competition[],
  loadState: 'loading' as LoadState,
})

function resetFilters() {
  state.search = ''
  state.statusFilter = 'all'
  state.modeFilter = 'all'
}

async function loadCompetitions() {
  state.loadState = 'loading'

  try {
    const response = await competitionApi.list<Competition[]>()
    state.competitions = Array.isArray(response) ? response : []
    updateListState()
  }
  catch {
    state.competitions = []
    state.visibleCompetitions = []
    state.loadState = 'error'
  }
}

function updateListState() {
  const q = state.search.trim().toLowerCase()
  state.visibleCompetitions = state.competitions.filter((competition) => {
    const matchesSearch = !q || competition.title.toLowerCase().includes(q) || (competition.description ?? '').toLowerCase().includes(q)
    const matchesStatus = state.statusFilter === 'all' || competition.status.toLowerCase() === state.statusFilter
    const matchesMode = state.modeFilter === 'all' || (competition.gameModeType ?? '').toLowerCase() === state.modeFilter
    return matchesSearch && matchesStatus && matchesMode
  })

  if (state.competitions.length === 0)
    state.loadState = 'empty'
  else if (state.visibleCompetitions.length === 0)
    state.loadState = 'filtered-empty'
  else
    state.loadState = 'ready'
}

watch(() => [state.search, state.statusFilter, state.modeFilter], () => {
  if (state.loadState !== 'loading' && state.loadState !== 'error')
    updateListState()
})

onMounted(() => {
  void loadCompetitions()
})
</script>

<template>
  <AppLayout>
    <div class="mx-auto w-full max-w-[1600px] space-y-6 px-4 py-6 md:px-6 lg:px-8">
      <div class="flex flex-col justify-between gap-4 md:flex-row md:items-end">
        <PageHeader
          :title="t('competitions.title')"
          :description="t('competitions.subtitle')"
          class="flex-1"
        />
      </div>

      <CompetitionsExplorer
        v-model:search="state.search"
        v-model:status-filter="state.statusFilter"
        v-model:mode-filter="state.modeFilter"
        :visible-competitions="state.visibleCompetitions"
        :load-state="state.loadState"
        @refresh="loadCompetitions"
        @reset-filters="resetFilters"
      />
    </div>
  </AppLayout>
</template>