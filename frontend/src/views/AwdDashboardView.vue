<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute } from 'vue-router'
import { useQuery, useQueryClient } from '@tanstack/vue-query'
import { competitionApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import { useAuthStore } from '@/stores/auth'
import RoundTimer from '@/components/game/RoundTimer.vue'
import ServiceStatusGrid from '@/components/game/ServiceStatusGrid.vue'
import type { ServiceStatus } from '@/components/game/ServiceStatusGrid.vue'
import AttackLogFeed from '@/components/game/AttackLogFeed.vue'
import type { AttackLogDto } from '@/components/game/AttackLogFeed.vue'
import { useSignalR } from '@/composables/useSignalR'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Badge } from '@/components/ui/badge'
import { 
  Select, 
  SelectContent, 
  SelectGroup, 
  SelectItem, 
  SelectTrigger, 
  SelectValue 
} from '@/components/ui/select'
import { toast } from 'vue-sonner'
import { Loader2, Upload, CheckCircle2 } from 'lucide-vue-next'

const { t } = useI18n()
const qc = useQueryClient()

const props = withDefaults(defineProps<{ gameModeType?: string }>(), { gameModeType: 'Awd' })
const isAwdp = computed(() => props.gameModeType?.toLowerCase() === 'awdp')

const route = useRoute()
const auth = useAuthStore()
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
const signalR = useSignalR({
  hubUrl: '/hubs/game',
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

function onPatchFileChange(e: Event) {
  const input = e.target as HTMLInputElement
  patchFile.value = input.files?.[0] ?? null
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
      description: data?.submissionId ? `Submission ID: ${data.submissionId}` : undefined
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
      toast.error(data?.message ?? t('challenges.incorrectFlag'))
    }
  } catch {
    toast.error(t('challenges.submissionFailed'))
  } finally {
    flagLoading.value = false
  }
}
</script>

<template>
  <div class="noctf-page-wide">
    <div class="noctf-panel rounded-xl p-5">
      <RoundTimer
        :round="round"
        :remaining-seconds="remainingSeconds"
        :total-seconds="totalSeconds"
      />
    </div>

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
                  @keydown.enter="submitFlag"
                  :disabled="flagLoading"
                />
                <Button
                  :disabled="flagLoading || !flagInput.trim() || !selectedChallenge"
                  @click="submitFlag"
                  class="shrink-0"
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
          <Card v-if="isAwdp" class="noctf-panel border-blue-500/10">
            <CardHeader>
              <CardTitle class="flex items-center gap-2 text-blue-600 dark:text-blue-400">
                <Upload class="size-5" />
                {{ t('awd.uploadPatch') }}
              </CardTitle>
            </CardHeader>
            <CardContent class="space-y-4">
              <div class="space-y-2">
                <label class="text-xs font-bold uppercase text-muted-foreground">{{ t('common.challenge') }}</label>
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
                class="group relative flex flex-col items-center justify-center gap-3 rounded-xl border border-dashed p-8 transition-all hover:bg-muted/50"
                :class="[
                  isDragOver ? 'border-primary bg-primary/5' : 'border-muted-foreground/25',
                  patchFile ? 'bg-muted/30' : ''
                ]"
                @dragover.prevent="isDragOver = true"
                @dragleave.prevent="isDragOver = false"
                @drop.prevent="isDragOver = false; patchFile = $event.dataTransfer?.files[0] || null"
                @click="($refs.patchFileInput as HTMLInputElement)?.click()"
              >
                <div class="flex size-10 items-center justify-center rounded-full bg-background shadow-sm border group-hover:scale-110 transition-transform">
                  <Upload class="size-5 text-muted-foreground" />
                </div>
                <div class="text-center">
                  <p class="text-sm font-medium">{{ patchFile ? patchFile.name : t('awd.dropFile') }}</p>
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
                variant="secondary"
                class="w-full bg-blue-500 text-white hover:bg-blue-600 dark:bg-blue-600 dark:hover:bg-blue-700"
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
              <CardTitle class="text-sm font-bold uppercase text-muted-foreground">{{ t('awd.patchStatus') }}</CardTitle>
            </CardHeader>
            <CardContent>
              <div class="divide-y">
                <div
                  v-for="ps in patchStatuses"
                  :key="ps.challengeId"
                  class="flex items-center justify-between py-2.5 first:pt-0 last:pb-0"
                >
                  <span class="text-sm font-medium">{{ ps.challengeName ?? ps.challengeTitle ?? ps.challengeId }}</span>
                  <Badge
                    :variant="ps.status === 'Verified' ? 'default' : ps.status === 'Rejected' || ps.status === 'Failed' ? 'destructive' : 'secondary'"
                    class="text-[10px] uppercase font-bold tracking-tighter h-5"
                  >
                    {{ ps.status }}
                  </Badge>
                </div>
              </div>
            </CardContent>
          </Card>
        </transition>
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
.fade-enter-active, .fade-leave-active { transition: opacity 0.3s ease; }
.fade-enter-from, .fade-leave-to { opacity: 0; }

.slide-up-enter-active, .slide-up-leave-active { transition: all 0.4s cubic-bezier(0.16, 1, 0.3, 1); }
.slide-up-enter-from { opacity: 0; transform: translateY(20px); }
.slide-up-leave-to { opacity: 0; transform: translateY(-20px); }

</style>
