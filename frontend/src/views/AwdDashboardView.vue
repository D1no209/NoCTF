<script setup lang="ts">
import type { AttackLogDto } from '@/components/game/AttackLogFeed.vue'
import type { AwdAwarenessEvent } from '@/components/game/AwdBattlefieldCore.vue'
import type { ServiceStatus } from '@/components/game/ServiceStatusGrid.vue'
import { useQuery, useQueryClient } from '@tanstack/vue-query'
import { AlertCircle, CheckCircle2, Loader2, RefreshCw, Upload } from 'lucide-vue-next'
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
import { Badge } from '@/components/ui/badge'
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
import { runtimeStatusVariant } from '@/lib/statusTones'
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

const {
  data: patchSubmissions,
  refetch: refetchPatchSubmissions,
  isError: patchSubmissionsError,
} = useQuery({
  queryKey: computed(() => queryKeys.patchSubmissions(competitionId.value)),
  queryFn: () => competitionApi.patchSubmissions<PatchSubmissionStatus[]>(competitionId.value),
  enabled: computed(() => !!competitionId.value && isAwdp.value),
  refetchInterval: computed(() => (isAwdp.value ? 10_000 : false)),
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

const patchChallenge = ref('')
const patchFile = ref<File | null>(null)
const patchLoading = ref(false)
const isDragOver = ref(false)
const patchStatuses = computed(() => patchSubmissions.value ?? [])
const controlDataError = computed(() => teamsError.value || challengesError.value)

async function refetchAwdData() {
  await Promise.all([
    refetchDashboard(),
    refetchTeams(),
    refetchChallenges(),
    isAwdp.value ? refetchPatchSubmissions() : Promise.resolve(),
  ])
}

function onPatchFileChange(e: Event) {
  const input = e.target as HTMLInputElement
  patchFile.value = input.files?.[0] ?? null
}

function onPatchDrop(e: DragEvent) {
  isDragOver.value = false
  patchFile.value = e.dataTransfer?.files[0] ?? null
}

async function submitPatch() {
  if (!patchFile.value || !patchChallenge.value) return
  patchLoading.value = true
  try {
    const data = await competitionApi.submitPatch<{ submissionId?: string }>(
      competitionId.value,
      patchChallenge.value,
      patchFile.value,
    )
    toast.success(t('awd.patchSubmitted'), {
      description: data?.submissionId ? `Submission ID: ${data.submissionId}` : undefined,
    })
    qc.invalidateQueries({ queryKey: queryKeys.patchSubmissions(competitionId.value) })
    patchFile.value = null
  } catch {
    toast.error(t('awd.patchUploadFailed'))
  } finally {
    patchLoading.value = false
  }
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

        <!-- Upload Patch (AWDP) -->
        <transition name="slide-up">
          <Card v-if="isAwdp" class="noctf-panel border-info/20">
            <CardHeader>
              <CardTitle class="flex items-center gap-2 text-info">
                <Upload class="size-5" />
                {{ t('awd.uploadPatch') }}
              </CardTitle>
            </CardHeader>
            <CardContent class="space-y-4">
              <div class="space-y-2">
                <label class="text-xs font-bold uppercase text-muted-foreground">{{
                  t('common.challenge')
                }}</label>
                <Select v-model="patchChallenge">
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

              <div
                class="group relative flex flex-col items-center justify-center gap-3 rounded-xl border border-dashed p-8 transition-[background-color,border-color,transform] duration-[var(--motion-fast)] ease-[var(--ease-out-quint)] hover:-translate-y-0.5 hover:bg-muted/50"
                :class="[
                  isDragOver ? 'border-primary bg-primary/5' : 'border-muted-foreground/25',
                  patchFile ? 'bg-muted/30' : '',
                ]"
                @dragover.prevent="isDragOver = true"
                @dragleave.prevent="isDragOver = false"
                @drop.prevent="onPatchDrop"
                @click="($refs.patchFileInput as HTMLInputElement)?.click()"
              >
                <div
                  class="flex size-10 items-center justify-center rounded-full border bg-background transition-colors duration-[var(--motion-fast)] ease-[var(--ease-out-quint)] group-hover:border-primary/30"
                >
                  <Upload class="size-5 text-muted-foreground" />
                </div>
                <div class="text-center">
                  <p class="text-sm font-medium">
                    {{ patchFile ? patchFile.name : t('awd.dropFile') }}
                  </p>
                  <p class="text-xs text-muted-foreground mt-1">.tar.gz or .tgz max 10MB</p>
                </div>
                <input
                  ref="patchFileInput"
                  type="file"
                  accept=".tar.gz,.tgz"
                  class="sr-only"
                  @change="onPatchFileChange"
                />
              </div>

              <Button
                class="w-full bg-info text-info-foreground hover:bg-info/90"
                :disabled="patchLoading || !patchFile || !patchChallenge"
                @click="submitPatch"
              >
                <Loader2 v-if="patchLoading" class="mr-2 size-4 animate-spin" />
                {{ t('awd.submitPatch') }}
              </Button>
            </CardContent>
          </Card>
        </transition>

        <!-- Patch Status List (AWDP) -->
        <transition name="fade">
          <Card v-if="isAwdp && patchStatuses.length > 0" class="noctf-panel">
            <CardHeader class="pb-2">
              <CardTitle class="text-sm font-bold uppercase text-muted-foreground">
                {{ t('awd.patchStatus') }}
              </CardTitle>
            </CardHeader>
            <CardContent>
              <div class="divide-y">
                <div
                  v-for="ps in patchStatuses"
                  :key="ps.challengeId"
                  class="flex items-center justify-between py-2.5 first:pt-0 last:pb-0"
                >
                  <span class="text-sm font-medium">{{
                    ps.challengeName ?? ps.challengeTitle ?? ps.challengeId
                  }}</span>
                  <Badge
                    :variant="runtimeStatusVariant(ps.status)"
                    class="text-[10px] uppercase font-bold tracking-tighter h-5"
                  >
                    {{ ps.status }}
                  </Badge>
                </div>
              </div>
            </CardContent>
          </Card>
        </transition>
        <Alert v-if="isAwdp && patchSubmissionsError" variant="destructive">
          {{ t('awd.patchStatusLoadFailed') }}
        </Alert>
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
