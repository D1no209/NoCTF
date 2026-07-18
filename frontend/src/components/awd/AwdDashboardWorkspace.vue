<script setup lang="ts">
import type { AttackLogDto } from '@/components/game/AttackLogFeed.vue'
import type { AwdAwarenessEvent } from '@/components/game/AwdBattlefieldCore.vue'
import type { ServiceStatus } from '@/components/game/ServiceStatusGrid.vue'
import { useQuery, useQueryClient } from '@tanstack/vue-query'
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
import AwdFlagSubmissionCard from '@/components/awd/AwdFlagSubmissionCard.vue'
import AwdpPatchStatusList from '@/components/awdp/AwdpPatchStatusList.vue'
import AwdpPatchUploadCard from '@/components/awdp/AwdpPatchUploadCard.vue'
import { useSignalR } from '@/composables/useSignalR'
import { Card } from '@/components/ui/card'
import { useAuthStore } from '@/stores/auth'
import { useScoreStore } from '@/stores/score'

const props = withDefaults(defineProps<{ gameModeType?: string }>(), { gameModeType: 'Awd' })
const { t } = useI18n()
const qc = useQueryClient()

const isAwdp = computed(() => props.gameModeType?.toLowerCase() === 'awdp')

const route = useRoute()
const auth = useAuthStore()
const scoreStore = useScoreStore()
const competitionId = computed(() => route.params.id as string)

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

interface PatchSubmissionStatus {
  id?: string
  submissionId?: string
  challengeId: string
  challengeName?: string
  challengeTitle?: string
  status: 'Pending' | 'Running' | 'Retrying' | 'Applied' | 'Verified' | 'Rejected' | 'Failed'
  submittedAt?: string
  createdAt?: string
  lastError?: string
  validationLog?: string
}

const { data: dashboard, refetch: refetchDashboard } = useQuery({
  queryKey: computed(() => queryKeys.awdDashboard(competitionId.value)),
  queryFn: () => competitionApi.awdDashboard<AwdDashboardResponse>(competitionId.value),
  enabled: computed(() => !!competitionId.value),
  refetchInterval: 10_000,
})

const { data: teams } = useQuery({
  queryKey: computed(() => queryKeys.teams(competitionId.value)),
  queryFn: () => competitionApi.teams<Team[]>(competitionId.value),
  enabled: computed(() => !!competitionId.value),
})

const { data: challenges } = useQuery({
  queryKey: computed(() => queryKeys.challenges(competitionId.value)),
  queryFn: () => competitionApi.challenges<Challenge[]>(competitionId.value),
  enabled: computed(() => !!competitionId.value),
})

const { data: patchSubmissions } = useQuery({
  queryKey: computed(() => queryKeys.patchSubmissions(competitionId.value)),
  queryFn: () => competitionApi.patchSubmissions<PatchSubmissionStatus[]>(competitionId.value),
  enabled: computed(() => !!competitionId.value && isAwdp.value),
  refetchInterval: computed(() => isAwdp.value ? 10_000 : false),
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

signalR.onRoundStarted(() => {
  refetchDashboard()
})

signalR.onAttackLog((log) => {
  attackLogs.value.unshift(log)
  if (attackLogs.value.length > 100)
    attackLogs.value.splice(100)
})

onMounted(() => {
  signalR.start()
})

onUnmounted(() => {
  signalR.stop()
})

const patchChallenge = ref('')
const patchFile = ref<File | null>(null)
const patchLoading = ref(false)
const isDragOver = ref(false)
const patchStatuses = computed(() => patchSubmissions.value ?? [])

async function submitPatch() {
  if (!patchFile.value || !patchChallenge.value)
    return
  patchLoading.value = true
  try {
    const data = await competitionApi.submitPatch<{ submissionId?: string }>(
      competitionId.value,
      scoreStore.teamId ?? '',
      patchChallenge.value,
      patchFile.value,
    )
    toast.success(t('awd.patchSubmitted'), {
      description: data?.submissionId ? t('awd.patchSubmissionId', { id: data.submissionId }) : undefined,
    })
    qc.invalidateQueries({ queryKey: queryKeys.patchSubmissions(competitionId.value) })
    patchFile.value = null
  }
  catch {
    toast.error(t('awd.patchUploadFailed'))
  }
  finally {
    patchLoading.value = false
  }
}

const selectedVictim = ref('')
const selectedChallenge = ref('')
const flagInput = ref('')
const flagLoading = ref(false)

async function submitFlag() {
  if (!flagInput.value.trim() || !selectedChallenge.value)
    return
  flagLoading.value = true
  try {
    const data = await competitionApi.submitFlag<{ correct?: boolean, message?: string }>(
      competitionId.value,
      scoreStore.teamId ?? '',
      selectedChallenge.value,
      flagInput.value.trim(),
    )
    if (data?.correct) {
      toast.success(t('challenges.correctFlag'))
      flagInput.value = ''
    }
    else {
      enqueueAttackFailed(data?.message ?? t('challenges.incorrectFlag'))
      toast.error(data?.message ?? t('challenges.incorrectFlag'))
    }
  }
  catch {
    toast.error(t('challenges.submissionFailed'))
  }
  finally {
    flagLoading.value = false
  }
}

function enqueueAttackFailed(reason: string) {
  const timestamp = new Date().toISOString()
  const victim = teams.value?.find(team => team.id === selectedVictim.value)
  const challenge = challenges.value?.find(item => item.id === selectedChallenge.value)

  localAwarenessEvents.value.unshift({
    id: `local-attack-failed:${competitionId.value}:${selectedVictim.value || 'unknown'}:${selectedChallenge.value}:${timestamp}`,
    type: 'attack',
    result: 'failed',
    attackerTeamId: scoreStore.teamId ?? undefined,
    attackerTeamName: scoreStore.myTeamName ?? auth.user?.userName ?? t('awd.currentTeam'),
    victimTeamId: victim?.id ?? selectedVictim.value,
    victimTeamName: victim?.name ?? t('awd.selectedTarget'),
    challengeId: challenge?.id ?? selectedChallenge.value,
    challengeName: challenge?.title ?? t('awd.selectedService'),
    round: round.value,
    timestamp,
    reason,
  })

  if (localAwarenessEvents.value.length > 50)
    localAwarenessEvents.value.splice(50)
}
</script>

<template>
  <div class="mx-auto w-full max-w-[1700px] space-y-6 px-4 py-6 md:px-6 lg:px-8">
    <Card class="px-4 py-4">
      <RoundTimer
        :round="round"
        :remaining-seconds="remainingSeconds"
        :total-seconds="totalSeconds"
      />
    </Card>

    <AwdBattlefieldCore
      :round="round"
      :services="services"
      :attack-logs="attackLogs"
      :external-events="localAwarenessEvents"
    />

    <div class="grid w-full grid-cols-12 gap-6">
      <div class="col-span-12 space-y-4 lg:col-span-3">
        <h2 class="px-1">
          {{ t('awd.serviceStatus') }}
        </h2>
        <ServiceStatusGrid :services="services" />
      </div>

      <div class="col-span-12 space-y-6 lg:col-span-6">
        <AwdFlagSubmissionCard
          v-model:selected-victim="selectedVictim"
          v-model:selected-challenge="selectedChallenge"
          v-model:flag-input="flagInput"
          :teams="teams ?? []"
          :challenges="challenges ?? []"
          :loading="flagLoading"
          @submit="submitFlag"
        />

        <transition name="slide-up">
          <AwdpPatchUploadCard
            v-if="isAwdp"
            v-model:patch-challenge="patchChallenge"
            v-model:patch-file="patchFile"
            v-model:is-drag-over="isDragOver"
            :challenges="challenges ?? []"
            :patch-loading="patchLoading"
            @submit="submitPatch"
          />
        </transition>

        <transition name="fade">
          <AwdpPatchStatusList
            v-if="isAwdp"
            :patch-statuses="patchStatuses"
          />
        </transition>
      </div>

      <div class="col-span-12 space-y-4 lg:col-span-3">
        <h2 class="px-1">
          {{ t('awd.realtimeActivity') }}
        </h2>
        <AttackLogFeed :logs="attackLogs" />
      </div>
    </div>
  </div>
</template>

<style scoped>
.fade-enter-active, .fade-leave-active { transition: opacity 0.3s ease; }
.fade-enter-from, .fade-leave-to { opacity: 0; }

.slide-up-enter-active, .slide-up-leave-active { transition: all 0.4s cubic-bezier(0.16, 1, 0.3, 1); }
.slide-up-enter-from { opacity: 0; transform: translateY(20px); }
.slide-up-leave-to { opacity: 0; transform: translateY(-20px); }
</style>
