<script setup lang="ts">
import type {
  NoCtfApplicationScoringLeaderboardLeaderboardEntry,
} from '@/api/generated/types.gen'
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { competitionApi } from '@/api/noctf'
import { Badge } from '@/components/ui/badge'
import { Card } from '@/components/ui/card'
import HandDrawnUnderline from '@/components/ui/hand-drawn/HandDrawnUnderline.vue'
import { COMPETITION_HUB_PATH, useSignalR } from '@/composables/useSignalR'
import { useAuthStore } from '@/stores/auth'
import { useScoreStore } from '@/stores/score'
import {
  asLeaderboardSnapshot,
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

const { t } = useI18n()
const auth = useAuthStore()
const scoreStore = useScoreStore()
const entries = ref<LeaderboardEntry[]>([])
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

function applyLeaderboard(nextEntries: LeaderboardEntry[]) {
  entries.value = nextEntries
  scoreStore.updateFromLeaderboard(entries.value, props.teamId, props.competitionId)
  emit('scoreUpdate', entries.value)
}

async function fetchLeaderboard() {
  try {
    const leaderboard = asLeaderboardSnapshot(
      await competitionApi.leaderboard(props.competitionId),
    )
    if (leaderboard)
      applyLeaderboard(leaderboard.entries ?? [])
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
        <Badge v-if="isConnected && !usingFallback" class="bg-green-600 text-white border-transparent text-xs">
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

    <div v-else-if="sortedEntries.length === 0">
      <Card class="relative flex min-h-48 flex-col items-center justify-center gap-2 border-2 border-dashed border-foreground/20 bg-[#FDFBF7] p-6 text-center dark:bg-[#1C1917]">
        <span class="text-4xl">📋</span>
        <p class="text-sm font-medium text-muted-foreground">
          {{ t('scoreboard.empty') }}
        </p>
      </Card>
    </div>

    <div v-else class="space-y-3">
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
