<script setup lang="ts">
import { computed, onMounted, onUnmounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, RouterLink } from 'vue-router'
import { useQuery } from '@tanstack/vue-query'
import { client } from '@/api/generated/client.gen'
import { useAuthStore } from '@/stores/auth'
import NavBar from '@/components/layout/NavBar.vue'
import KohStatusCard from '@/components/game/KohStatusCard.vue'
import type { KohChallengeStatus } from '@/components/game/KohStatusCard.vue'
import { useSignalR } from '@/composables/useSignalR'
import { Badge } from '@/components/ui/badge'

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
  queryKey: computed(() => ['koh-dashboard', competitionId.value]),
  queryFn: async () => {
    const res = await client.get<{ 200: KohDashboardResponse }, unknown, false>({
      url: '/api/competitions/{id}/koh-dashboard',
      path: { id: competitionId.value },
    })
    return res.data ?? null
  },
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
  <div class="min-h-screen flex flex-col bg-background">
    <NavBar />

    <!-- Header -->
    <header class="border-b bg-card px-6 py-4">
      <div class="max-w-[1600px] mx-auto flex items-center justify-between gap-4">
        <div class="flex items-center gap-3">
          <RouterLink
            :to="`/competitions/${competitionId}`"
            class="text-sm text-muted-foreground hover:text-foreground transition-colors"
          >
            ← {{ t('nav.back') }}
          </RouterLink>
          <Badge variant="outline" class="font-mono text-xs">{{ t('koh.badge') }}</Badge>
          <Badge variant="secondary" class="text-xs">{{ t('koh.kingOfTheHill') }}</Badge>
        </div>
        <div class="text-sm text-muted-foreground">
          {{ t('koh.challengeCount', { count: challenges.length }) }}
        </div>
      </div>
    </header>

    <!-- Main content -->
    <div class="flex-1 max-w-[1600px] mx-auto w-full p-4">
      <!-- Loading state -->
      <div v-if="!dashboard" class="flex items-center justify-center h-64 text-muted-foreground">
        {{ t('koh.loading') }}
      </div>

      <!-- Empty state -->
      <div
        v-else-if="challenges.length === 0"
        class="flex items-center justify-center h-64 text-muted-foreground"
      >
        {{ t('koh.empty') }}
      </div>

      <!-- Challenge grid -->
      <div
        v-else
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
    </div>
  </div>
</template>
