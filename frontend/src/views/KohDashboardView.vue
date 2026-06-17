<script setup lang="ts">
import { computed, onMounted, onUnmounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute } from 'vue-router'
import { useQuery } from '@tanstack/vue-query'
import { competitionApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import { useAuthStore } from '@/stores/auth'
import AppLayout from '@/components/layout/AppLayout.vue'
import PageHeader from '@/components/layout/PageHeader.vue'
import KohStatusCard from '@/components/game/KohStatusCard.vue'
import type { KohChallengeStatus } from '@/components/game/KohStatusCard.vue'
import { useSignalR } from '@/composables/useSignalR'
import { Badge } from '@/components/ui/badge'
import DataState from '@/components/state/DataState.vue'

const { t } = useI18n()
const route = useRoute()
const auth = useAuthStore()
const competitionId = computed(() => route.params.id as string)

// ── Dashboard data ──────────────────────────────────────────────────────────

interface KohDashboardResponse {
  competitionId: string
  challenges: KohChallengeStatus[]
}

const { data: dashboard, refetch } = useQuery({
  queryKey: computed(() => queryKeys.kohDashboard(competitionId.value)),
  queryFn: () => competitionApi.kohDashboard<KohDashboardResponse>(competitionId.value),
  enabled: computed(() => !!competitionId.value),
  refetchInterval: 15_000,
})

const challenges = computed<KohChallengeStatus[]>(() => dashboard.value?.challenges ?? [])

// ── SignalR ─────────────────────────────────────────────────────────────────

const signalR = useSignalR({
  hubUrl: '/hubs/game',
  accessToken: () => auth.accessToken,
})

signalR.onKohUpdate((dto) => {
  // Optimistically update the matching challenge card
  const ch = challenges.value.find((c) => c.challengeId === dto.challengeId)
  if (ch) {
    ch.controllerTeamId = dto.controllerTeamId
    ch.controllerTeamName = dto.controllerTeamName
    ch.controlStartTime = dto.timestamp
    ch.controlDurationSeconds = 0
    if (dto.controllerTeamId) {
      ch.history.unshift({
        teamId: dto.controllerTeamId,
        teamName: dto.controllerTeamName,
        startTime: dto.timestamp,
        endTime: null,
      })
    }
  } else {
    refetch()
  }
})

onMounted(() => signalR.start())
onUnmounted(() => signalR.stop())
</script>

<template>
  <AppLayout>
    <div class="mx-auto w-full max-w-[1600px] space-y-4 px-4 py-4">
      <PageHeader
        :title="t('koh.kingOfTheHill')"
        :description="t('koh.challengeCount', { count: challenges.length })"
        :back-to="`/competitions/${competitionId}`"
        :back-label="t('nav.back')"
      >
        <template #actions>
          <Badge variant="outline" class="font-mono text-xs">{{ t('koh.badge') }}</Badge>
          <Badge variant="secondary" class="text-xs">{{ t('koh.kingOfTheHill') }}</Badge>
        </template>
      </PageHeader>

      <!-- Challenge grid -->
      <DataState
        :loading="!dashboard"
        :empty="!!dashboard && challenges.length === 0"
        :loading-title="t('koh.loading')"
        :empty-title="t('koh.empty')"
      >
        <div
          class="grid gap-4"
          :class="[
            challenges.length === 1 ? 'grid-cols-1 max-w-sm' :
            challenges.length === 2 ? 'grid-cols-1 sm:grid-cols-2 max-w-2xl' :
            'grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4'
          ]"
        >
          <KohStatusCard
            v-for="ch in challenges"
            :key="ch.challengeId"
            :status="ch"
          />
        </div>
      </DataState>
    </div>
  </AppLayout>
</template>
