<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useQuery } from '@tanstack/vue-query'
import { Blocks } from 'lucide-vue-next'
import { competitionApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import CommandCompetitionCatalog from '../components/CommandCompetitionCatalog.vue'
import type { CommandCompetition } from '../components/CommandCompetitionCard.vue'
import CommandSignal from '../primitives/CommandSignal.vue'

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
    <header class="v2-competitions__heading">
      <div>
        <CommandSignal label="Competition service / live contract data" tone="success" />
        <h1>Competition registry</h1>
        <p>Filter and enter the competitions exposed by the platform service.</p>
      </div>
      <div class="v2-competitions__count">
        <Blocks class="size-4" />
        <strong>{{ String(competitions?.length ?? 0).padStart(2, '0') }}</strong>
        <span>records</span>
      </div>
    </header>

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
.v2-competitions__heading { display: flex; align-items: end; justify-content: space-between; gap: 20px; }
.v2-competitions__heading h1 { margin: 7px 0 0; color: var(--v2-text); font-size: 24px; font-weight: 680; letter-spacing: 0; }
.v2-competitions__heading p { margin: 7px 0 0; color: var(--v2-text-muted); font-size: 12px; }
.v2-competitions__count { display: grid; grid-template-columns: auto auto; align-items: center; column-gap: 8px; border-left: 1px solid var(--v2-line); padding-left: 13px; color: var(--v2-primary); }
.v2-competitions__count strong { color: var(--v2-text); font-family: ui-monospace, SFMono-Regular, Menlo, monospace; font-size: 22px; line-height: 1; }
.v2-competitions__count span { grid-column: 2; color: var(--v2-text-muted); font-family: ui-monospace, SFMono-Regular, Menlo, monospace; font-size: 9px; font-weight: 700; letter-spacing: 0.08em; text-transform: uppercase; }

@media (max-width: 560px) {
  .v2-competitions__heading { align-items: flex-start; flex-direction: column; }
  .v2-competitions__count { border-left: 0; padding-left: 0; }
}
</style>
