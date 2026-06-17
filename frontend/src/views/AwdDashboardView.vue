<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, RouterLink } from 'vue-router'
import { useQuery } from '@tanstack/vue-query'
import { client } from '@/api/generated/client.gen'
import { useAuthStore } from '@/stores/auth'
import NavBar from '@/components/layout/NavBar.vue'
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

const { t } = useI18n()

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

const { data: dashboard, refetch: refetchDashboard } = useQuery({
  queryKey: computed(() => ['awd-dashboard', competitionId.value]),
  queryFn: async () => {
    const res = await client.get<{ 200: AwdDashboardResponse }, unknown, false>({
      url: '/api/competitions/{id}/awd-dashboard',
      path: { id: competitionId.value },
    })
    return res.data ?? null
  },
  enabled: computed(() => !!competitionId.value),
  refetchInterval: 10_000,
})

const { data: teams } = useQuery({
  queryKey: computed(() => ['teams', competitionId.value]),
  queryFn: async () => {
    const res = await client.get<{ 200: Team[] }, unknown, false>({
      url: '/api/competitions/{id}/teams',
      path: { id: competitionId.value },
    })
    return res.data ?? []
  },
  enabled: computed(() => !!competitionId.value),
})

const { data: challenges } = useQuery({
  queryKey: computed(() => ['challenges', competitionId.value]),
  queryFn: async () => {
    const res = await client.get<{ 200: Challenge[] }, unknown, false>({
      url: '/api/competitions/{id}/challenges',
      path: { id: competitionId.value },
    })
    return res.data ?? []
  },
  enabled: computed(() => !!competitionId.value),
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

// Patch status list per challenge
interface PatchSubmissionStatus {
  challengeId: string
  challengeName: string
  status: 'Pending' | 'Verified' | 'Rejected'
  submittedAt: string
}
const patchStatuses = ref<PatchSubmissionStatus[]>([])

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
    const form = new FormData()
    form.append('file', patchFile.value)
    const res = await fetch(
      `/api/competitions/${competitionId.value}/challenges/${patchChallenge.value}/patch`,
      {
        method: 'POST',
        headers: { Authorization: `Bearer ${auth.accessToken}` },
        body: form,
      }
    )
    if (res.status === 202) {
      const data = await res.json()
      patchStatus.value = 'success'
      patchSubmissionId.value = data.submissionId
      patchMessage.value = t('awd.patchSubmitted')
      // Add to patch status list
      const ch = challenges.value?.find((c) => c.id === patchChallenge.value)
      const existing = patchStatuses.value.findIndex((p) => p.challengeId === patchChallenge.value)
      const entry: PatchSubmissionStatus = {
        challengeId: patchChallenge.value,
        challengeName: ch?.title ?? patchChallenge.value,
        status: 'Pending',
        submittedAt: new Date().toISOString(),
      }
      if (existing >= 0) patchStatuses.value.splice(existing, 1, entry)
      else patchStatuses.value.push(entry)
    } else {
      patchStatus.value = 'error'
      patchMessage.value = t('awd.uploadFailed', { status: res.status })
    }
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
  if (!flagInput.value.trim()) return
  submitStatus.value = 'loading'
  submitMessage.value = ''
  try {
    const res = await client.post<{ 200: { correct: boolean; message?: string } }, unknown, false>({
      url: '/api/competitions/{id}/submit',
      path: { id: competitionId.value },
      body: {
        flagContent: flagInput.value.trim(),
        challengeId: selectedChallenge.value || undefined,
      },
    })
    const data = res.data
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
  <div class="min-h-screen flex flex-col bg-background">
    <NavBar />

    <!-- Header: round timer -->
    <header class="border-b bg-card px-6 py-4">
      <div class="max-w-[1600px] mx-auto flex items-center justify-between gap-4">
        <div class="flex items-center gap-3">
          <RouterLink
            :to="`/competitions/${competitionId}`"
            class="text-sm text-muted-foreground hover:text-foreground transition-colors"
          >
            ← {{ t('nav.back') }}
          </RouterLink>
          <Badge variant="outline" class="font-mono text-xs">AWD</Badge>
          <Badge v-if="isAwdp" variant="secondary" class="text-xs">{{ t('awd.defensePhase') }}</Badge>
        </div>
        <RoundTimer
          :round="round"
          :remaining-seconds="remainingSeconds"
          :total-seconds="totalSeconds"
        />
        <div class="w-24" />
      </div>
    </header>

    <!-- Main grid -->
    <div class="flex-1 max-w-[1600px] mx-auto w-full grid grid-cols-12 gap-4 p-4">
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
              <select
                v-model="selectedVictim"
                class="w-full h-9 rounded-md border border-input bg-background px-3 py-1 text-sm shadow-sm focus:outline-none focus:ring-1 focus:ring-ring"
              >
                <option value="">{{ t('awd.selectTeam') }}</option>
                <option v-for="team in teams" :key="team.id" :value="team.id">
                  {{ team.name }}
                </option>
              </select>
            </div>

            <!-- Challenge select -->
            <div class="space-y-1">
              <label class="text-sm font-medium text-foreground">{{ t('common.challenge') }}</label>
              <select
                v-model="selectedChallenge"
                class="w-full h-9 rounded-md border border-input bg-background px-3 py-1 text-sm shadow-sm focus:outline-none focus:ring-1 focus:ring-ring"
              >
                <option value="">{{ t('awd.selectChallenge') }}</option>
                <option v-for="ch in challenges" :key="ch.id" :value="ch.id">
                  {{ ch.title }}
                </option>
              </select>
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
        <Card class="mt-4">
          <CardHeader>
            <CardTitle>{{ t('awd.uploadPatch') }}</CardTitle>
          </CardHeader>
          <CardContent class="space-y-3">
            <!-- Challenge select -->
            <div class="space-y-1">
              <label class="text-sm font-medium text-foreground">{{ t('common.challenge') }}</label>
              <select
                v-model="patchChallenge"
                class="w-full h-9 rounded-md border border-input bg-background px-3 py-1 text-sm shadow-sm focus:outline-none focus:ring-1 focus:ring-ring"
              >
                <option value="">{{ t('awd.selectChallenge') }}</option>
                <option v-for="ch in challenges" :key="ch.id" :value="ch.id">
                  {{ ch.title }}
                </option>
              </select>
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
                <span class="truncate font-medium">{{ ps.challengeName }}</span>
                <Badge
                  :class="[
                    ps.status === 'Verified' ? 'bg-green-500/15 text-green-600 border-green-500/30' :
                    ps.status === 'Rejected' ? 'bg-red-500/15 text-red-600 border-red-500/30' :
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
</template>
