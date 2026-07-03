<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import { useQuery } from '@tanstack/vue-query'
import { competitionApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import CompetitionDetailView from './CompetitionDetailView.vue'
import AwdDashboardView from './AwdDashboardView.vue'
import KohDashboardView from './KohDashboardView.vue'

const route = useRoute()
const id = computed(() => route.params.id as string)

interface CompetitionDetail {
  id: string
  gameModeType: string
}

const { data: competition } = useQuery({
  queryKey: computed(() => queryKeys.competition(id.value)),
  queryFn: () => competitionApi.get<CompetitionDetail>(id.value),
  enabled: computed(() => !!id.value),
})

const mode = computed(() => competition.value?.gameModeType?.toLowerCase())
</script>

<template>
  <CompetitionDetailView v-if="mode === 'ctf' || mode === 'awdp'" />
  <AwdDashboardView v-else-if="mode === 'awd'" />
  <KohDashboardView v-else-if="mode === 'koh'" />
  <div v-else class="flex items-center justify-center min-h-screen text-muted-foreground">
    {{ $t('common.loading') }}
  </div>
</template>
