<script setup lang="ts">
import DataState from '@/ui-v1/components/state/DataState.vue'
import { useCompetitionGateway } from '@/features/competitions/useCompetitionGateway'
import CompetitionDetailView from './CompetitionDetailView.vue'
import AwdDashboardView from './AwdDashboardView.vue'
import KohDashboardView from './KohDashboardView.vue'
import PenetrationView from './PenetrationView.vue'

const {
  gameMode: mode,
  isLoading,
  isError,
  error,
  refetch,
} = useCompetitionGateway()
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
