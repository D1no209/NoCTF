<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute } from 'vue-router'
import { useQuery, useQueryClient } from '@tanstack/vue-query'
import { competitionApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import { useAuthStore } from '@/stores/auth'
import AppLayout from '@/components/layout/AppLayout.vue'
import PageHeader from '@/components/layout/PageHeader.vue'
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
import { Select } from '@/components/ui/select'

const { t } = useI18n()
const qc = useQueryClient()

// Props: optional gameModeType to show Defense Phase badge
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

// ── Derived state ───────────────────────────────────────────────────────────

const round = computed(() => dashboard.value?.currentRound ?? 0)
const remainingSeconds = computed(() => dashboard.value?.remainingSeconds ?? 0)
const totalSeconds = computed(() => dashboard.value?.roundDurationSeconds ?? 300)
const services = computed<ServiceStatus[]>(() => dashboard.value?.services ?? [])

// ── Attack log (real-time via SignalR) ──────────────────────────────────────

const attackLogs = ref<AttackLogDto[]>([])

const signalR = useSignalR({
  hubUrl: '/hubs/game',
  accessToken: () => auth.accessToken,
})

signalR.onRoundStarted((_roundNumber) => {
  refetchDashboard()
})

signalR.onAttackLog((log) => {
  attackLogs.value.push(log)
  // Keep last 100 entries
  if (attackLogs.value.length > 100) {
    attackLogs.value.splice(0, attackLogs.value.length - 100)
  }
})

onMounted(() => {
  signalR.start()
})

onUnmounted(() => {
  signalR.stop()
})

// ── Patch submission ────────────────────────────────────────────────────────

const patchChallenge = ref('')
const patchFile = ref<File | null>(null)
const patchStatus = ref<'idle' | 'loading' | 'success' | 'error'>('idle')
const patchMessage = ref('')
const patchSubmissionId = ref('')
const isDragOver = ref(false)

const patchStatuses = computed(() => patchSubmissions.value ?? [])

function onPatchFileChange(e: Event) {
  const input = e.target as HTMLInputElement
  patchFile.value = input.files?.[0] ?? null
}

function onDragOver(e: DragEvent) {
  e.preventDefault()
  isDragOver.value = true
}

function onDragLeave() {
  isDragOver.value = false
}

function onDrop(e: DragEvent) {
  e.preventDefault()
  isDragOver.value = false
  const file = e.dataTransfer?.files?.[0]
  if (file) patchFile.value = file
}

async function submitPatch() {
  if (!patchFile.value || !patchChallenge.value) return
  patchStatus.value = 'loading'
  patchMessage.value = ''
  patchSubmissionId.value = ''
  try {
    const data = await competitionApi.submitPatch<{ submissionId?: string }>(
      competitionId.value,
      patchChallenge.value,
      patchFile.value,
    )
      patchStatus.value = 'success'
      patchSubmissionId.value = data?.submissionId ?? ''
      patchMessage.value = t('awd.patchSubmitted')
      qc.invalidateQueries({ queryKey: queryKeys.patchSubmissions(competitionId.value) })
  } catch {
    patchStatus.value = 'error'
    patchMessage.value = t('awd.patchUploadFailed')
  }
}

const selectedVictim = ref('')
const selectedChallenge = ref('')
const flagInput = ref('')
const submitStatus = ref<'idle' | 'loading' | 'success' | 'error'>('idle')
const submitMessage = ref('')

async function submitFlag() {
  if (!flagInput.value.trim() || !selectedChallenge.value) return
  submitStatus.value = 'loading'
  submitMessage.value = ''
  try {
    const data = await competitionApi.submitFlag<{ correct?: boolean; message?: string }>(
      competitionId.value,
      selectedChallenge.value,
      flagInput.value.trim(),
    )
    if (data?.correct) {
      submitStatus.value = 'success'
      submitMessage.value = t('challenges.correctFlag')
      flagInput.value = ''
    } else {
      submitStatus.value = 'error'
      submitMessage.value = data?.message ?? t('challenges.incorrectFlag')
    }
  } catch {
    submitStatus.value = 'error'
    submitMessage.value = t('challenges.submissionFailed')
  }
}
</script>

<template>
  <AppLayout>
    <div class="mx-auto flex w-full max-w-[1600px] flex-col gap-4 px-4 py-4">
      <PageHeader
        title="AWD"
        :back-to="`/competitions/${competitionId}`"
        :back-label="t('nav.back')"
      >
        <template #actions>
          <Badge v-if="isAwdp" variant="secondary" class="text-xs">{{ t('awd.defensePhase') }}</Badge>
          <Badge variant="outline" class="font-mono text-xs">AWD</Badge>
        </template>
      </PageHeader>

      <div class="rounded-md border bg-card px-4 py-3">
        <RoundTimer
          :round="round"
          :remaining-seconds="remainingSeconds"
          :total-seconds="totalSeconds"
        />
      </div>

      <!-- Main grid -->
      <div class="grid w-full grid-cols-12 gap-4">
      <!-- Left: Service Status -->
      <div class="col-span-12 lg:col-span-3">
        <ServiceStatusGrid :services="services" />
      </div>

      <!-- Center: Flag Submission -->
      <div class="col-span-12 lg:col-span-6">
        <Card>
          <CardHeader>
            <CardTitle>{{ t('awd.submitFlag') }}</CardTitle>
          </CardHeader>
          <CardContent class="space-y-3">
            <!-- Victim team select -->
            <div class="space-y-1">
              <label class="text-sm font-medium text-foreground">{{ t('awd.victimTeam') }}</label>
              <Select
                v-model="selectedVictim"
              >
                <option value="">{{ t('awd.selectTeam') }}</option>
                <option v-for="team in teams" :key="team.id" :value="team.id">
                  {{ team.name }}
                </option>
              </Select>
            </div>

            <!-- Challenge select -->
            <div class="space-y-1">
              <label class="text-sm font-medium text-foreground">{{ t('common.challenge') }}</label>
              <Select
                v-model="selectedChallenge"
              >
                <option value="">{{ t('awd.selectChallenge') }}</option>
                <option v-for="ch in challenges" :key="ch.id" :value="ch.id">
                  {{ ch.title }}
                </option>
              </Select>
            </div>

            <!-- Flag input -->
            <div class="space-y-1">
              <label class="text-sm font-medium text-foreground">{{ t('awd.flag') }}</label>
              <Input
                v-model="flagInput"
                :placeholder="t('challenges.flagPlaceholder')"
                class="font-mono"
                @keydown.enter="submitFlag"
              />
            </div>

            <Button
              class="w-full"
              :disabled="submitStatus === 'loading' || !flagInput.trim()"
              @click="submitFlag"
            >
              {{ submitStatus === 'loading' ? t('common.submitting') : t('awd.submitFlag') }}
            </Button>

            <!-- Status message -->
            <p
              v-if="submitMessage"
              class="text-sm text-center"
              :class="submitStatus === 'success' ? 'text-green-500' : 'text-red-500'"
            >
              {{ submitMessage }}
            </p>
          </CardContent>
        </Card>

        <!-- Upload Patch (AWDP) -->
        <Card v-if="isAwdp" class="mt-4">
          <CardHeader>
            <CardTitle>{{ t('awd.uploadPatch') }}</CardTitle>
          </CardHeader>
          <CardContent class="space-y-3">
            <!-- Challenge select -->
            <div class="space-y-1">
              <label class="text-sm font-medium text-foreground">{{ t('common.challenge') }}</label>
              <Select
                v-model="patchChallenge"
              >
                <option value="">{{ t('awd.selectChallenge') }}</option>
                <option v-for="ch in challenges" :key="ch.id" :value="ch.id">
                  {{ ch.title }}
                </option>
              </Select>
            </div>

            <!-- Drag-and-drop file area -->
            <div class="space-y-1">
              <label class="text-sm font-medium text-foreground">{{ t('awd.patchArchive') }}</label>
              <div
                class="relative flex flex-col items-center justify-center gap-2 rounded-md border-2 border-dashed px-4 py-6 text-sm transition-colors cursor-pointer"
                :class="isDragOver ? 'border-primary bg-primary/5 text-primary' : 'border-input text-muted-foreground hover:border-primary/50'"
                @dragover="onDragOver"
                @dragleave="onDragLeave"
                @drop="onDrop"
                @click="($refs.patchFileInput as HTMLInputElement)?.click()"
              >
                <span v-if="patchFile" class="font-medium text-foreground truncate max-w-full">{{ patchFile.name }}</span>
                <span v-else>{{ t('awd.dropFile') }}</span>
                <input
                  ref="patchFileInput"
                  type="file"
                  accept=".tar.gz,.tgz"
                  class="sr-only"
                  @change="onPatchFileChange"
                />
              </div>
            </div>

            <Button
              class="w-full"
              variant="outline"
              :disabled="patchStatus === 'loading' || !patchFile || !patchChallenge"
              @click="submitPatch"
            >
              {{ patchStatus === 'loading' ? t('common.uploading') : t('awd.submitPatch') }}
            </Button>

            <!-- Status message -->
            <p
              v-if="patchMessage"
              class="text-sm text-center"
              :class="patchStatus === 'success' ? 'text-green-500' : 'text-red-500'"
            >
              {{ patchMessage }}
            </p>
            <p v-if="patchSubmissionId" class="text-xs text-muted-foreground text-center font-mono">
              ID: {{ patchSubmissionId }}
            </p>
          </CardContent>
        </Card>

        <!-- Patch Status List (AWDP) -->
        <Card v-if="patchStatuses.length > 0" class="mt-4">
          <CardHeader>
            <CardTitle class="text-sm">{{ t('awd.patchStatus') }}</CardTitle>
          </CardHeader>
          <CardContent>
            <ul class="space-y-2">
              <li
                v-for="ps in patchStatuses"
                :key="ps.challengeId"
                class="flex items-center justify-between gap-2 text-sm"
              >
                <span class="truncate font-medium">{{ ps.challengeName ?? ps.challengeTitle ?? ps.challengeId }}</span>
                <Badge
                  :class="[
                    ps.status === 'Verified' ? 'bg-green-500/15 text-green-600 border-green-500/30' :
                    ['Rejected', 'Failed'].includes(ps.status) ? 'bg-red-500/15 text-red-600 border-red-500/30' :
                    'bg-yellow-500/15 text-yellow-600 border-yellow-500/30'
                  ]"
                  variant="outline"
                  class="shrink-0 text-xs font-mono"
                >
                  {{ ps.status }}
                </Badge>
              </li>
            </ul>
          </CardContent>
        </Card>
      </div>

      <!-- Right: Attack Log -->
      <div class="col-span-12 lg:col-span-3">
        <AttackLogFeed :logs="attackLogs" />
      </div>
      </div>
    </div>
  </AppLayout>
</template>
