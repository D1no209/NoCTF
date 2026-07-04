<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import { useQuery } from '@tanstack/vue-query'
import { ApiError, competitionApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import CompetitionDetailView from './CompetitionDetailView.vue'
import AwdDashboardView from './AwdDashboardView.vue'
import KohDashboardView from './KohDashboardView.vue'
import NotFoundView from './NotFoundView.vue'
import { Button } from '@/components/ui/button'

const route = useRoute()
const id = computed(() => route.params.id as string)

interface CompetitionDetail {
  id: string
  gameModeType: string
}

const {
  data: competition,
  isLoading,
  isError,
  error,
  refetch,
} = useQuery({
  queryKey: computed(() => queryKeys.competition(id.value)),
  queryFn: () => competitionApi.get<CompetitionDetail>(id.value),
  enabled: computed(() => !!id.value),
})

const mode = computed(() => competition.value?.gameModeType?.toLowerCase())
const isNotFound = computed(() => error.value instanceof ApiError && error.value.status === 404)
</script>

<template>
  <NotFoundView v-if="isNotFound" />
  <CompetitionDetailView v-else-if="mode === 'ctf' || mode === 'awdp'" />
  <AwdDashboardView v-else-if="mode === 'awd'" />
  <KohDashboardView v-else-if="mode === 'koh'" />
  <div v-else-if="isLoading" class="flex items-center justify-center min-h-screen text-muted-foreground">
    {{ $t('common.loading') }}
  </div>
  <div
    v-else-if="isError"
    class="mx-auto flex min-h-[60vh] max-w-lg flex-col items-center justify-center gap-4 px-4 text-center"
  >
    <div>
      <h1 class="text-xl font-semibold">{{ $t('errors.loadFailed') }}</h1>
      <p class="mt-2 text-sm text-muted-foreground">
        {{ $t('errors.genericError') }}
      </p>
    </div>
    <Button type="button" variant="outline" @click="refetch()">
      {{ $t('common.refresh') }}
    </Button>
  </div>
  <NotFoundView v-else />
</template>
