<script setup lang="ts">
import type { AttackLogDto } from '@/components/game/AttackLogFeed.vue'
import type { AwdAwarenessEvent } from '@/components/game/AwdBattlefieldCore.vue'
import type { ServiceStatus } from '@/components/game/ServiceStatusGrid.vue'
import { useQuery } from '@tanstack/vue-query'
import { AlertCircle, CheckCircle2, Loader2, RefreshCw } from 'lucide-vue-next'
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute } from 'vue-router'
import { toast } from 'vue-sonner'
import { competitionApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import AttackLogFeed from '@/components/game/AttackLogFeed.vue'
import AwdBattlefieldCore from '@/components/game/AwdBattlefieldCore.vue'
import RoundTimer from '@/components/game/RoundTimer.vue'
import ServiceStatusGrid from '@/components/game/ServiceStatusGrid.vue'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Alert } from '@/components/ui/alert'
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { useSignalR } from '@/composables/useSignalR'
import { useAuthStore } from '@/stores/auth'
import { useScoreStore } from '@/stores/score'

const { t } = useI18n()

const route = useRoute()
const auth = useAuthStore()
const scoreStore = useScoreStore()
const competitionId = computed(() => route.params.id as string)

// ── Dashboard data ──────────────────────────────────────────────────────────

interface AwdDashboardResponse {
  competitionId: string
  currentRound: number
  roundDurationSeconds: number
  remainingSeconds: number
  services: ServiceStatus[]
}

interface Team {
  id: string
  name: string
}

interface Challenge {
  id: string
  title: string
}

const {
  data: dashboard,
  refetch: refetchDashboard,
  isLoading: dashboardLoading,
  isError: dashboardError,
} = useQuery({
  queryKey: computed(() => queryKeys.awdDashboard(competitionId.value)),
  queryFn: () => competitionApi.awdDashboard<AwdDashboardResponse>(competitionId.value),
  enabled: computed(() => !!competitionId.value),
  refetchInterval: 10_000,
})

const {
  data: teams,
  refetch: refetchTeams,
  isError: teamsError,
} = useQuery({
  queryKey: computed(() => queryKeys.teams(competitionId.value)),
  queryFn: () => competitionApi.teams<Team[]>(competitionId.value),
  enabled: computed(() => !!competitionId.value),
})

const {
  data: challenges,
  refetch: refetchChallenges,
  isError: challengesError,
} = useQuery({
  queryKey: computed(() => queryKeys.challenges(competitionId.value)),
  queryFn: () => competitionApi.challenges<Challenge[]>(competitionId.value),
  enabled: computed(() => !!competitionId.value),
})

const round = computed(() => dashboard.value?.currentRound ?? 0)
const remainingSeconds = computed(() => dashboard.value?.remainingSeconds ?? 0)
const totalSeconds = computed(() => dashboard.value?.roundDurationSeconds ?? 300)
const services = computed<ServiceStatus[]>(() => dashboard.value?.services ?? [])

const attackLogs = ref<AttackLogDto[]>([])
const localAwarenessEvents = ref<AwdAwarenessEvent[]>([])
const signalR = useSignalR({
  hubUrl: `/hubs/game?competitionId=${competitionId.value}`,
  accessToken: () => auth.accessToken,
})

signalR.onRoundStarted((_roundNumber) => {
  refetchDashboard()
})

signalR.onAttackLog((log) => {
  attackLogs.value.unshift(log)
  if (attackLogs.value.length > 100) {
    attackLogs.value.splice(100)
  }
})

onMounted(() => {
  signalR.start()
})

onUnmounted(() => {
  signalR.stop()
})

const controlDataError = computed(() => teamsError.value || challengesError.value)

async function refetchAwdData() {
  await Promise.all([
    refetchDashboard(),
    refetchTeams(),
    refetchChallenges(),
  ])
}

const selectedVictim = ref('')
const selectedChallenge = ref('')
const flagInput = ref('')
const flagLoading = ref(false)

async function submitFlag() {
  if (!flagInput.value.trim() || !selectedChallenge.value) return
  flagLoading.value = true
  try {
    const data = await competitionApi.submitFlag<{ correct?: boolean; message?: string }>(
      competitionId.value,
      selectedChallenge.value,
      flagInput.value.trim(),
    )
    if (data?.correct) {
      toast.success(t('challenges.correctFlag'))
      flagInput.value = ''
    } else {
      enqueueAttackFailed(data?.message ?? t('challenges.incorrectFlag'))
      toast.error(data?.message ?? t('challenges.incorrectFlag'))
    }
  } catch {
    toast.error(t('challenges.submissionFailed'))
  } finally {
    flagLoading.value = false
  }
}

function enqueueAttackFailed(reason: string) {
  const timestamp = new Date().toISOString()
  const victim = teams.value?.find((team) => team.id === selectedVictim.value)
  const challenge = challenges.value?.find((item) => item.id === selectedChallenge.value)

  localAwarenessEvents.value.unshift({
    id: `local-attack-failed:${competitionId.value}:${selectedVictim.value || 'unknown'}:${selectedChallenge.value}:${timestamp}`,
    type: 'attack',
    result: 'failed',
    attackerTeamId: scoreStore.teamId ?? undefined,
    attackerTeamName: scoreStore.myTeamName ?? auth.user?.userName ?? 'Current team',
    victimTeamId: victim?.id ?? selectedVictim.value,
    victimTeamName: victim?.name ?? 'Selected target',
    challengeId: challenge?.id ?? selectedChallenge.value,
    challengeName: challenge?.title ?? 'Selected service',
    round: round.value,
    timestamp,
    reason,
  })

  if (localAwarenessEvents.value.length > 50) localAwarenessEvents.value.splice(50)
}
</script>

<template>
  <div class="noctf-page-wide">
    <div v-if="dashboardError" class="noctf-state-box py-12">
      <AlertCircle class="size-8 text-danger" />
      <div class="space-y-1 text-center">
        <p class="font-medium">{{ t('awd.dashboardLoadFailed') }}</p>
        <p class="text-sm text-muted-foreground">{{ t('awd.dashboardLoadFailedDetail') }}</p>
      </div>
      <Button type="button" variant="outline" size="sm" @click="refetchAwdData">
        <RefreshCw class="size-4" />
        {{ t('common.refresh') }}
      </Button>
    </div>

    <div v-else-if="dashboardLoading" class="noctf-state-box py-12 text-muted-foreground">
      <Loader2 class="size-8 animate-spin" />
      <p class="text-sm">{{ t('common.loading') }}</p>
    </div>

    <div v-else class="noctf-panel rounded-xl p-5">
      <RoundTimer
        :round="round"
        :remaining-seconds="remainingSeconds"
        :total-seconds="totalSeconds"
      />
    </div>

    <AwdBattlefieldCore
      v-if="!dashboardError"
      :round="round"
      :services="services"
      :attack-logs="attackLogs"
      :external-events="localAwarenessEvents"
    />

    <!-- Main grid -->
    <div class="grid w-full grid-cols-12 gap-6">
      <!-- Left: Service Status -->
      <div class="col-span-12 lg:col-span-3 space-y-4">
        <h2 class="noctf-label px-1">
          {{ t('awd.serviceStatus') }}
        </h2>
        <ServiceStatusGrid :services="services" />
      </div>

      <!-- Center: Controls -->
      <div class="col-span-12 lg:col-span-6 space-y-6">
        <!-- Submit Flag -->
        <Card class="noctf-panel border-primary/10">
          <CardHeader>
            <CardTitle class="flex items-center gap-2">
              <CheckCircle2 class="size-5 text-primary" />
              {{ t('awd.submitFlag') }}
            </CardTitle>
          </CardHeader>
          <CardContent class="space-y-4">
            <Alert v-if="controlDataError" variant="destructive">
              {{ t('awd.controlDataLoadFailed') }}
            </Alert>
            <div class="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div class="space-y-2">
                <label class="noctf-label">{{ t('awd.victimTeam') }}</label>
                <Select v-model="selectedVictim">
                  <SelectTrigger>
                    <SelectValue :placeholder="t('awd.selectTeam')" />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectGroup>
                      <SelectItem v-for="team in teams" :key="team.id" :value="team.id">
                        {{ team.name }}
                      </SelectItem>
                    </SelectGroup>
                  </SelectContent>
                </Select>
              </div>

              <div class="space-y-2">
                <label class="noctf-label">{{ t('common.challenge') }}</label>
                <Select v-model="selectedChallenge">
                  <SelectTrigger>
                    <SelectValue :placeholder="t('awd.selectChallenge')" />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectGroup>
                      <SelectItem v-for="ch in challenges" :key="ch.id" :value="ch.id">
                        {{ ch.title }}
                      </SelectItem>
                    </SelectGroup>
                  </SelectContent>
                </Select>
              </div>
            </div>

            <div class="space-y-2">
              <label class="noctf-label">{{ t('awd.flag') }}</label>
              <div class="flex gap-2">
                <Input
                  v-model="flagInput"
                  :placeholder="t('challenges.flagPlaceholder')"
                  class="font-mono"
                  :disabled="flagLoading"
                  @keydown.enter="submitFlag"
                />
                <Button
                  :disabled="flagLoading || !flagInput.trim() || !selectedChallenge"
                  class="shrink-0"
                  @click="submitFlag"
                >
                  <Loader2 v-if="flagLoading" class="mr-2 size-4 animate-spin" />
                  {{ t('awd.submitFlag') }}
                </Button>
              </div>
            </div>
          </CardContent>
        </Card>

      </div>

      <!-- Right: Attack Log -->
      <div class="col-span-12 lg:col-span-3 space-y-4">
        <h2 class="noctf-label px-1">
          {{ t('awd.realtimeActivity') }}
        </h2>
        <AttackLogFeed :logs="attackLogs" />
      </div>
    </div>
  </div>
</template>

<style scoped>
.fade-enter-active,
.fade-leave-active {
  transition:
    opacity var(--motion-fast) var(--ease-out-quint),
    transform var(--motion-fast) var(--ease-out-quint);
}
.fade-enter-from {
  opacity: 0;
  transform: translateY(4px);
}
.fade-leave-to {
  opacity: 0;
  transform: translateY(-2px);
}

.slide-up-enter-active,
.slide-up-leave-active {
  transition:
    opacity var(--motion-standard) var(--ease-out-expo),
    transform var(--motion-standard) var(--ease-out-expo);
}
.slide-up-enter-from {
  opacity: 0;
  transform: translateY(10px);
}
.slide-up-leave-to {
  opacity: 0;
  transform: translateY(-6px);
}
</style>
