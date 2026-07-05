<script setup lang="ts">
import type { KohChallengeStatus } from '@/components/game/KohStatusCard.vue'
import { vAutoAnimate } from '@formkit/auto-animate/vue'
import { useQuery } from '@tanstack/vue-query'
import { ShieldAlert, Trophy } from 'lucide-vue-next'
import { computed, onMounted, onUnmounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute } from 'vue-router'
import { competitionApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import KohStatusCard from '@/components/game/KohStatusCard.vue'
import PageHeader from '@/components/layout/PageHeader.vue'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { useSignalR } from '@/composables/useSignalR'
import { useAuthStore } from '@/stores/auth'

const { t } = useI18n()
const route = useRoute()
const auth = useAuthStore()
const competitionId = computed(() => route.params.id as string)

interface KohDashboardResponse {
  competitionId: string
  challenges: KohChallengeStatus[]
}

const {
  data: dashboard,
  refetch,
  isLoading,
  isError,
} = useQuery({
  queryKey: computed(() => queryKeys.kohDashboard(competitionId.value)),
  queryFn: () => competitionApi.kohDashboard<KohDashboardResponse>(competitionId.value),
  enabled: computed(() => !!competitionId.value),
  refetchInterval: 15_000,
})

const challenges = computed<KohChallengeStatus[]>(() => dashboard.value?.challenges ?? [])

const signalR = useSignalR({
  hubUrl: '/hubs/game',
  accessToken: () => auth.accessToken,
})

signalR.onKohUpdate((dto) => {
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
      if (ch.history.length > 50) ch.history.pop()
    }
  } else {
    refetch()
  }
})

onMounted(() => signalR.start())
onUnmounted(() => signalR.stop())
</script>

<template>
  <div class="noctf-page">
    <div class="flex flex-col md:flex-row md:items-end justify-between gap-4">
      <PageHeader :title="t('koh.kingOfTheHill')" :description="t('koh.subtitle')">
        <template #actions>
          <div class="flex items-center gap-2">
            <Badge variant="warning" class="animate-pulse px-3">
              <Trophy class="mr-1.5 size-3.5" />
              {{ t('common.live') }}
            </Badge>
            <Badge
              variant="outline"
              class="font-mono text-[10px] uppercase tracking-widest bg-muted/30"
            >
              {{ t('common.mode') }}: KOH
            </Badge>
          </div>
        </template>
      </PageHeader>
    </div>

    <!-- Content Area -->
    <div
      v-if="isLoading"
      class="grid gap-6 grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4"
    >
      <Skeleton v-for="i in 4" :key="i" class="h-64 rounded-xl" />
    </div>

    <div v-else-if="isError" class="noctf-state-box py-20">
      <div
        class="size-16 rounded-full bg-danger-muted flex items-center justify-center text-danger mb-4"
      >
        <ShieldAlert class="size-8" />
      </div>
      <h3 class="text-xl font-bold">
        {{ t('koh.loadError') }}
      </h3>
      <p class="text-muted-foreground mt-2 max-w-xs">
        {{ t('koh.loadErrorDetail') }}
      </p>
      <Button variant="outline" class="mt-6" @click="refetch">
        {{ t('common.refresh') }}
      </Button>
    </div>

    <div v-else-if="challenges.length === 0" class="noctf-state-box py-20">
      <div
        class="size-16 rounded-full bg-muted flex items-center justify-center text-muted-foreground mb-4"
      >
        <Trophy class="size-8" />
      </div>
      <h3 class="text-xl font-bold">
        {{ t('koh.empty') }}
      </h3>
      <p class="text-muted-foreground mt-2 max-w-xs">
        {{ t('koh.emptyDetail') }}
      </p>
    </div>

    <div
      v-else
      v-auto-animate="{ duration: 180, easing: 'cubic-bezier(0.22, 1, 0.36, 1)' }"
      class="grid gap-6"
      :class="[
        challenges.length === 1
          ? 'grid-cols-1 max-w-xl mx-auto'
          : challenges.length === 2
            ? 'grid-cols-1 md:grid-cols-2 max-w-4xl mx-auto'
            : 'grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4',
      ]"
    >
      <KohStatusCard
        v-for="ch in challenges"
        :key="ch.challengeId"
        :status="ch"
        class="transition-[background-color,border-color,transform] duration-[var(--motion-fast)] ease-[var(--ease-out-quint)] hover:-translate-y-0.5 hover:border-primary/20 hover:bg-accent/35"
      />
    </div>
  </div>
</template>
