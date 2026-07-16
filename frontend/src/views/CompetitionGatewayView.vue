<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import { useQuery } from '@tanstack/vue-query'
import { competitionApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import DataState from '@/components/state/DataState.vue'
import CompetitionDetailView from './CompetitionDetailView.vue'
import AwdDashboardView from './AwdDashboardView.vue'
import KohDashboardView from './KohDashboardView.vue'
import PenetrationView from './PenetrationView.vue'

const route = useRoute()
const id = computed(() => route.params.id as string)

interface CompetitionDetail {
  id: string
  gameModeType: string
}

const {
  data: competition,
  error,
  isError,
  isLoading,
  refetch,
} = useQuery({
  queryKey: computed(() => queryKeys.competition(id.value)),
  queryFn: () => competitionApi.get<CompetitionDetail>(id.value),
  enabled: computed(() => !!id.value),
})

const mode = computed(() => competition.value?.gameModeType?.toLowerCase())
</script>

<template>
  <div class="mx-auto flex min-h-screen w-full max-w-6xl items-center px-4 py-8">
    <DataState
      v-if="isLoading"
      class="w-full"
      loading
    />
    <DataState
      v-else-if="isError"
      class="w-full"
      error
      :error-message="error instanceof Error ? error.message : undefined"
      :retry-label="$t('common.refresh')"
      @retry="refetch"
    />
    <DataState
      v-else-if="mode !== 'ctf' && mode !== 'awdp' && mode !== 'awd' && mode !== 'koh' && mode !== 'penetration'"
      class="w-full"
      unsupported
    />
    <CompetitionDetailView v-else-if="mode === 'ctf' || mode === 'awdp'" class="w-full" />
    <AwdDashboardView v-else-if="mode === 'awd'" class="w-full" />
    <KohDashboardView v-else-if="mode === 'koh'" class="w-full" />
    <PenetrationView v-else class="w-full" />
  </div>
</template>
