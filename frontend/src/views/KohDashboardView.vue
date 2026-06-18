<script setup lang="ts">
import { computed, onMounted, onUnmounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute } from 'vue-router'
import { useQuery } from '@tanstack/vue-query'
import { competitionApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import { useAuthStore } from '@/stores/auth'
import PageHeader from '@/components/layout/PageHeader.vue'
import KohStatusCard from '@/components/game/KohStatusCard.vue'
import type { KohChallengeStatus } from '@/components/game/KohStatusCard.vue'
import { useSignalR } from '@/composables/useSignalR'
import { Badge } from '@/components/ui/badge'
import { Skeleton } from '@/components/ui/skeleton'
import { Button } from '@/components/ui/button'
import { vAutoAnimate } from '@formkit/auto-animate/vue'
import { Trophy, ShieldAlert } from 'lucide-vue-next'

const { t } = useI18n()
const route = useRoute()
const auth = useAuthStore()
const competitionId = computed(() => route.params.id as string)

interface KohDashboardResponse {
  competitionId: string
  challenges: KohChallengeStatus[]
}

const { data: dashboard, refetch, isLoading, isError } = useQuery({
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
  <div class="mx-auto w-full max-w-[1600px] space-y-8 px-4 py-8 md:px-6">
    <div class="flex flex-col md:flex-row md:items-end justify-between gap-4">
      <PageHeader
        :title="t('koh.kingOfTheHill')"
        :description="t('koh.subtitle', 'Control the hill and earn points over time')"
      >
        <template #actions>
          <div class="flex items-center gap-2">
            <Badge variant="secondary" class="animate-pulse bg-amber-500/10 text-amber-500 border-amber-500/20 px-3">
              <Trophy class="mr-1.5 size-3.5" />
              LIVE
            </Badge>
            <Badge variant="outline" class="font-mono text-[10px] uppercase tracking-widest bg-muted/30">
              MODE: KOH
            </Badge>
          </div>
        </template>
      </PageHeader>
    </div>

    <!-- Content Area -->
    <div v-if="isLoading" class="grid gap-6 grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
      <Skeleton v-for="i in 4" :key="i" class="h-64 rounded-xl" />
    </div>

    <div v-else-if="isError" class="flex flex-col items-center justify-center py-20 text-center">
      <div class="size-16 rounded-full bg-destructive/10 flex items-center justify-center text-destructive mb-4">
        <ShieldAlert class="size-8" />
      </div>
      <h3 class="text-xl font-bold">{{ t('koh.loadError') }}</h3>
      <p class="text-muted-foreground mt-2 max-w-xs">{{ t('koh.loadErrorDetail') }}</p>
      <Button variant="outline" class="mt-6" @click="refetch">{{ t('common.retry') }}</Button>
    </div>

    <div v-else-if="challenges.length === 0" class="flex flex-col items-center justify-center py-20 text-center border-2 border-dashed rounded-xl">
      <div class="size-16 rounded-full bg-muted flex items-center justify-center text-muted-foreground mb-4">
        <Trophy class="size-8" />
      </div>
      <h3 class="text-xl font-bold">{{ t('koh.empty') }}</h3>
      <p class="text-muted-foreground mt-2 max-w-xs">{{ t('koh.emptyDetail', 'No hills have been defined for this competition.') }}</p>
    </div>

    <div 
      v-else 
      v-auto-animate
      class="grid gap-6"
      :class="[
        challenges.length === 1 ? 'grid-cols-1 max-w-xl mx-auto' :
        challenges.length === 2 ? 'grid-cols-1 md:grid-cols-2 max-w-4xl mx-auto' :
        'grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4'
      ]"
    >
      <KohStatusCard
        v-for="ch in challenges"
        :key="ch.challengeId"
        :status="ch"
        class="transition-all duration-300 hover:shadow-xl hover:-translate-y-1"
      />
    </div>
  </div>
</template>
