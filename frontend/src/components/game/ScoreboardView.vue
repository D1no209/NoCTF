<script setup lang="ts">
import type {
  NoCtfApplicationScoringLeaderboardLeaderboardBloodSummary,
  NoCtfApplicationScoringLeaderboardLeaderboardEntry,
  NoCtfApplicationScoringLeaderboardLeaderboardResponse,
} from '@/api/generated/types.gen'
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { leaderboardDataScope, leaderboardVisibility, leaderboardVisibilityLabelKey } from '@/api/leaderboardVisibility'
import { competitionApi } from '@/api/noctf'
import { Badge } from '@/components/ui/badge'
import { Card } from '@/components/ui/card'
import HandDrawnUnderline from '@/components/ui/hand-drawn/HandDrawnUnderline.vue'
import { COMPETITION_HUB_PATH, useSignalR } from '@/composables/useSignalR'
import { useAuthStore } from '@/stores/auth'
import { useScoreStore } from '@/stores/score'
import {
  asLeaderboardSnapshot,
  leaderboardBloodRows,
  leaderboardRows,
} from './leaderboardPresentation'

type LeaderboardEntry = NoCtfApplicationScoringLeaderboardLeaderboardEntry

const props = defineProps<{
  competitionId: string
  teamId?: string | null
  full?: boolean
}>()

const emit = defineEmits<{
  scoreUpdate: [entries: LeaderboardEntry[]]
}>()

const { t, locale } = useI18n()
const auth = useAuthStore()
const scoreStore = useScoreStore()
const entries = ref<LeaderboardEntry[]>([])
const bloods = ref<NoCtfApplicationScoringLeaderboardLeaderboardBloodSummary[]>([])
const visibility = ref<NoCtfApplicationScoringLeaderboardLeaderboardResponse['visibility']>(
  leaderboardVisibility.normal,
)
const dataScope = ref<NoCtfApplicationScoringLeaderboardLeaderboardResponse['dataScope']>(
  leaderboardDataScope.live,
)
const dataAsOf = ref<string | null>(null)
const loading = ref(true)
const usingFallback = ref(false)
let pollInterval: ReturnType<typeof setInterval> | null = null
let heartbeatInterval: ReturnType<typeof setInterval> | null = null

const { connection, isConnected, start } = useSignalR({
  hubUrl: COMPETITION_HUB_PATH,
  accessToken: () => auth.accessToken,
  onConnected: () => {
    void joinCompetition()
  },
  onDisconnected: () => {
    stopHeartbeat()
    startPolling()
  },
  onReconnecting: () => {
    stopHeartbeat()
    startPolling()
  },
  onReconnected: () => {
    void joinCompetition()
  },
})

const sortedEntries = computed(() => leaderboardRows(entries.value))
const displayedBloods = computed(() => leaderboardBloodRows(bloods.value))
const isHidden = computed(() => dataScope.value === leaderboardDataScope.hidden)
const isFrozen = computed(() => dataScope.value === leaderboardDataScope.frozen)
const hasPrivilegedLiveView = computed(() =>
  dataScope.value === leaderboardDataScope.live
  && visibility.value !== leaderboardVisibility.normal,
)

const displayedEntries = computed(() => {
  if (props.full === false)
    return sortedEntries.value.slice(0, 10)
  return sortedEntries.value
})

function rankBadgeClass(rank?: number) {
  if (rank === 1)
    return 'bg-[#FFD700] text-[#5C4800] -rotate-2 shadow-sm'
  if (rank === 2)
    return 'bg-[#C0C0C0] text-[#3A3A3A] rotate-1 shadow-sm'
  if (rank === 3)
    return 'bg-[#CD7F32] text-white -rotate-1 shadow-sm'
  return 'bg-muted text-muted-foreground'
}

function rowTiltClass(index: number) {
  const tilts = [
    '-rotate-[0.3deg]',
    'rotate-[0.3deg]',
    '-rotate-[0.3deg]',
    'rotate-[0.3deg]',
  ]
  return tilts[index % tilts.length]
}

function formatBloodTime(value: string) {
  const date = new Date(value)
  if (Number.isNaN(date.getTime()))
    return value
  return new Intl.DateTimeFormat(locale.value, {
    dateStyle: 'short',
    timeStyle: 'medium',
  }).format(date)
}

function formatDataAsOf(value: string | null) {
  if (!value)
    return t('common.unknown')
  const date = new Date(value)
  if (Number.isNaN(date.getTime()))
    return value
  return new Intl.DateTimeFormat(locale.value, {
    dateStyle: 'medium',
    timeStyle: 'medium',
  }).format(date)
}

function applyLeaderboard(snapshot: NoCtfApplicationScoringLeaderboardLeaderboardResponse) {
  entries.value = snapshot.entries ?? []
  bloods.value = snapshot.bloods ?? []
  visibility.value = snapshot.visibility ?? leaderboardVisibility.normal
  dataScope.value = snapshot.dataScope ?? leaderboardDataScope.live
  dataAsOf.value = snapshot.dataAsOf ?? null
  scoreStore.updateFromLeaderboard(entries.value, props.teamId, props.competitionId)
  emit('scoreUpdate', entries.value)
}

async function fetchLeaderboard() {
  try {
    const leaderboard = asLeaderboardSnapshot(
      await competitionApi.leaderboard(props.competitionId),
    )
    if (leaderboard)
      applyLeaderboard(leaderboard)
  }
  catch {
    // Keep the last successful snapshot while polling.
  }
  finally {
    loading.value = false
  }
}

function startPolling() {
  if (pollInterval)
    return
  usingFallback.value = true
  pollInterval = setInterval(fetchLeaderboard, 10_000)
}

function stopPolling() {
  if (pollInterval) {
    clearInterval(pollInterval)
    pollInterval = null
  }
  usingFallback.value = false
}

function stopHeartbeat() {
  if (!heartbeatInterval)
    return
  clearInterval(heartbeatInterval)
  heartbeatInterval = null
}

async function heartbeatCompetition() {
  try {
    await connection.value?.invoke('HeartbeatCompetition', props.competitionId)
    stopPolling()
  }
  catch {
    startPolling()
  }
}

function startHeartbeat() {
  if (heartbeatInterval)
    return
  heartbeatInterval = setInterval(heartbeatCompetition, 30_000)
}

async function joinCompetition() {
  try {
    await connection.value?.invoke('JoinCompetition', props.competitionId)
    stopPolling()
    startHeartbeat()
    await fetchLeaderboard()
  }
  catch {
    stopHeartbeat()
    startPolling()
  }
}

watch(connection, (conn) => {
  if (!conn)
    return
  conn.on('leaderboardRefreshed', (notification: { competitionId?: string }) => {
    if (notification?.competitionId && notification.competitionId !== props.competitionId)
      return
    fetchLeaderboard()
  })
  conn.on('competitionLifecycleChanged', (notification: { competitionId?: string }) => {
    if (notification?.competitionId === props.competitionId)
      fetchLeaderboard()
  })
}, { immediate: true })

watch(() => props.teamId, () => {
  scoreStore.updateFromLeaderboard(entries.value, props.teamId, props.competitionId)
})

onMounted(async () => {
  await fetchLeaderboard()
  try {
    await start()
  }
  catch {
    startPolling()
  }
})

onUnmounted(() => {
  stopHeartbeat()
  stopPolling()
})
</script>

<template>
  <div class="space-y-5">
    <div class="flex items-center justify-between">
      <div>
        <h2 class="text-xl font-semibold">
          {{ t('scoreboard.title') }}
        </h2>
        <HandDrawnUnderline :width="120" color="#E63946" />
      </div>
      <div class="flex items-center gap-2">
        <Badge v-if="isHidden" variant="destructive" class="text-xs">
          {{ t(leaderboardVisibilityLabelKey(visibility)) }}
        </Badge>
        <Badge v-else-if="isFrozen" variant="secondary" class="text-xs">
          {{ t(leaderboardVisibilityLabelKey(visibility)) }}
        </Badge>
        <Badge v-else-if="isConnected && !usingFallback" class="bg-green-600 text-white border-transparent text-xs">
          {{ t('common.live') }}
        </Badge>
        <Badge v-else-if="usingFallback" variant="secondary" class="text-xs">
          {{ t('common.polling') }}
        </Badge>
        <Badge v-else variant="outline" class="text-xs text-muted-foreground">
          {{ t('common.connecting') }}
        </Badge>
      </div>
    </div>

    <div v-if="loading" class="text-sm text-muted-foreground">
      {{ t('scoreboard.loading') }}
    </div>

    <Card v-else-if="isHidden" class="relative flex min-h-56 flex-col items-center justify-center gap-3 border-2 border-dashed border-foreground/30 bg-muted/40 p-6 text-center">
      <span class="flex size-10 items-center justify-center border-2 border-foreground text-xl" aria-hidden="true">×</span>
      <div class="space-y-1">
        <p class="font-bold">
          {{ t('scoreboard.blackoutTitle') }}
        </p>
        <p class="mx-auto max-w-2xl text-sm text-muted-foreground">
          {{ t('scoreboard.blackoutDescription') }}
        </p>
      </div>
    </Card>

    <div v-else-if="sortedEntries.length === 0">
      <Card class="relative flex min-h-48 flex-col items-center justify-center gap-2 border-2 border-dashed border-foreground/20 bg-[#FDFBF7] p-6 text-center dark:bg-[#1C1917]">
        <span class="text-4xl">📋</span>
        <p class="text-sm font-medium text-muted-foreground">
          {{ t('scoreboard.empty') }}
        </p>
      </Card>
    </div>

    <div v-else class="space-y-3">
      <div v-if="isFrozen" class="flex flex-col gap-1 border-2 border-dashed border-border bg-muted/40 p-3 text-sm sm:flex-row sm:items-center sm:justify-between">
        <span class="font-bold">{{ t('scoreboard.frozenTitle') }}</span>
        <span class="text-muted-foreground">{{ t('scoreboard.dataAsOf', { time: formatDataAsOf(dataAsOf) }) }}</span>
      </div>
      <div v-else-if="hasPrivilegedLiveView" class="border-2 border-dashed border-border bg-muted/40 p-3 text-sm">
        <span class="font-bold">{{ t('scoreboard.privilegedLiveTitle') }}</span>
        <span class="ml-2 text-muted-foreground">{{ t('scoreboard.privilegedLiveDescription') }}</span>
      </div>
      <div v-if="displayedBloods.length" class="space-y-2">
        <h3 class="text-sm font-bold">
          {{ t('scoreboard.bloods') }}
        </h3>
        <div class="grid gap-2 lg:grid-cols-3">
          <div
            v-for="blood in displayedBloods"
            :key="blood.key"
            class="flex min-w-0 items-start gap-3 border-2 border-dashed border-foreground/10 bg-[#FDFBF7] px-3 py-2 dark:bg-[#1C1917]"
          >
            <div
              class="flex h-8 w-8 shrink-0 items-center justify-center rounded-md text-sm font-black shadow-sm"
              :class="rankBadgeClass(blood.bloodRank)"
              :aria-label="t(blood.labelKey)"
            >
              {{ blood.bloodRank }}
            </div>
            <div class="min-w-0 flex-1">
              <div class="flex min-w-0 items-baseline gap-2">
                <span class="shrink-0 text-xs font-bold">{{ t(blood.labelKey) }}</span>
                <span class="truncate font-semibold">{{ blood.teamName }}</span>
              </div>
              <div class="mt-1 flex min-w-0 items-center justify-between gap-3 text-xs text-muted-foreground">
                <span class="truncate font-mono" :title="blood.slotKey">{{ blood.slotKey }}</span>
                <time class="shrink-0 tabular-nums" :datetime="blood.occurredAt">
                  {{ formatBloodTime(blood.occurredAt) }}
                </time>
              </div>
            </div>
          </div>
        </div>
      </div>

      <div class="flex items-center justify-between">
        <div>
          <h3 class="text-sm font-bold">
            {{ t('scoreboard.fullBoard') }}
          </h3>
          <HandDrawnUnderline :width="100" color="#F4A261" />
        </div>
      </div>
      <div class="grid gap-2">
        <Card
          v-for="(entry, index) in displayedEntries"
          :key="entry.teamId ?? entry.rank"
          class="relative border-2 border-dashed border-foreground/10 bg-[#FDFBF7] px-3 py-2 dark:bg-[#1C1917]"
          :class="rowTiltClass(index)"
        >
          <div class="flex items-center gap-3">
            <div
              class="flex h-8 w-8 shrink-0 items-center justify-center rounded-md text-sm font-black shadow-sm"
              :class="rankBadgeClass(entry.rank)"
            >
              {{ entry.rank }}
            </div>
            <div class="min-w-0 flex-1">
              <div class="truncate font-semibold">
                {{ entry.teamName }}
              </div>
            </div>
            <div class="text-right">
              <div class="font-mono font-bold tabular-nums">
                {{ entry.totalScore }}
              </div>
              <div class="text-xs text-muted-foreground">
                {{ entry.solvedCount }} {{ t('scoreboard.solves') }}
              </div>
            </div>
          </div>
        </Card>
      </div>
    </div>
  </div>
</template>
