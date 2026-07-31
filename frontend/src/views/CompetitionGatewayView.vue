<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import { competitionApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import DataState from '@/components/state/DataState.vue'
import CompetitionDetailView from './CompetitionDetailView.vue'

const route = useRoute()
const id = computed(() => route.params.id as string)

const {
  data: competition,
  error,
  isError,
  isLoading,
  refetch,
} = useQuery({
  queryKey: computed(() => queryKeys.competition(id.value)),
  queryFn: () => competitionApi.get(id.value),
  enabled: computed(() => !!id.value),
})

const mode = computed(() => competition.value?.mode)
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
      v-else-if="mode !== 'ctf' && mode !== 'awdp' && mode !== 'awd' && mode !== 'koh'"
      class="w-full"
      unsupported
    />
    <CompetitionDetailView v-else class="w-full" />
  </div>
</template>
