<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useQuery } from '@tanstack/vue-query'
import { Blocks } from 'lucide-vue-next'
import { competitionApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import CommandCompetitionCatalog from '../components/CommandCompetitionCatalog.vue'
import type { CommandCompetition } from '../components/CommandCompetitionCard.vue'
import CommandPageHeader from '../components/CommandPageHeader.vue'

const router = useRouter()
const search = ref('')
const statusFilter = ref('all')
const modeFilter = ref('all')

const {
  data: competitions,
  isError,
  isFetching,
  isLoading,
  refetch,
} = useQuery({
  queryKey: queryKeys.competitions,
  queryFn: () => competitionApi.list<CommandCompetition[]>(),
})

const visibleCompetitions = computed(() => {
  const query = search.value.trim().toLowerCase()
  return (competitions.value ?? []).filter((competition) => {
    const searchMatches = !query
      || competition.title.toLowerCase().includes(query)
      || (competition.description ?? '').toLowerCase().includes(query)
    const statusMatches = statusFilter.value === 'all'
      || competition.status.toLowerCase() === statusFilter.value
    const modeMatches = modeFilter.value === 'all'
      || (competition.gameModeType ?? '').toLowerCase() === modeFilter.value
    return searchMatches && statusMatches && modeMatches
  })
})

const competitionCount = computed(() => (competitions.value ?? []).length)

const catalogState = computed(() => {
  if (isLoading.value)
    return 'loading'
  if (isError.value)
    return 'error'
  if (!(competitions.value ?? []).length)
    return 'empty'
  if (!visibleCompetitions.value.length)
    return 'filtered-empty'
  return 'ready'
})

function resetFilters() {
  search.value = ''
  statusFilter.value = 'all'
  modeFilter.value = 'all'
}

function openCompetition(id: string) {
  router.push(`/competitions/${id}`)
}

function registerForCompetition(id: string) {
  router.push(`/competitions/${id}/register`)
}
</script>

<template>
  <section class="v2-competitions">
    <CommandPageHeader
      signal-label="Competition service / live contract data"
      signal-tone="success"
      title="Competition registry"
      description="Filter and enter the competitions exposed by the platform service."
      :stat-icon="Blocks"
      :stat-value="String(competitionCount).padStart(2, '0')"
      stat-label="records"
    />

    <CommandCompetitionCatalog
      v-model:search="search"
      v-model:status-filter="statusFilter"
      v-model:mode-filter="modeFilter"
      :competitions="visibleCompetitions"
      :state="catalogState"
      :refreshing="isFetching"
      @refresh="refetch"
      @reset="resetFilters"
      @open="openCompetition"
      @register="registerForCompetition"
    />
  </section>
</template>

<style scoped>
.v2-competitions { display: grid; gap: 18px; }
</style>
