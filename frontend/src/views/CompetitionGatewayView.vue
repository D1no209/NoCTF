<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import { useQuery } from '@tanstack/vue-query'
import { client } from '@/api/generated/client.gen'
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
  queryKey: computed(() => ['competition', id.value]),
  queryFn: async () => {
    const res = await client.get<{ 200: CompetitionDetail }, unknown, false>({
      url: '/api/competitions/{id}',
      path: { id: id.value },
    })
    return res.data ?? null
  },
  enabled: computed(() => !!id.value),
})

const mode = computed(() => competition.value?.gameModeType?.toLowerCase())
</script>

<template>
  <CompetitionDetailView v-if="mode === 'ctf'" />
  <AwdDashboardView v-else-if="mode === 'awd'" />
  <AwdDashboardView v-else-if="mode === 'awdp'" game-mode-type="Awdp" />
  <KohDashboardView v-else-if="mode === 'koh'" />
  <div v-else class="flex items-center justify-center min-h-screen text-muted-foreground">
    {{ $t('common.loading') }}
  </div>
</template>
