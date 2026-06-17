<script setup lang="ts">
import { ref, onMounted, onUnmounted, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { client } from '@/api/generated/client.gen'
import { useAuthStore } from '@/stores/auth'
import { useScoreStore } from '@/stores/score'
import { useSignalR } from '@/composables/useSignalR'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import { Badge } from '@/components/ui/badge'
import type { NoCtfapiEndpointsCompetitionsLeaderboardEntryDto } from '@/api/generated/types.gen'

const props = defineProps<{
  competitionId: string
}>()

const emit = defineEmits<{
  scoreUpdate: [entries: NoCtfapiEndpointsCompetitionsLeaderboardEntryDto[]]
}>()

const { t } = useI18n()
const auth = useAuthStore()
const scoreStore = useScoreStore()
const entries = ref<NoCtfapiEndpointsCompetitionsLeaderboardEntryDto[]>([])
const loading = ref(true)
const usingFallback = ref(false)
let pollInterval: ReturnType<typeof setInterval> | null = null

async function fetchLeaderboard() {
  try {
    const res = await client.get<{ 200: { entries?: NoCtfapiEndpointsCompetitionsLeaderboardEntryDto[] } }, unknown, false>({
      url: '/api/competitions/{competitionId}/leaderboard',
      path: { competitionId: props.competitionId },
    })
    if (res.data?.entries) {
      entries.value = res.data.entries
      scoreStore.updateFromLeaderboard(entries.value)
      emit('scoreUpdate', entries.value)
    }
  } catch {
    // silently fail on poll
  } finally {
    loading.value = false
  }
}

function startPolling() {
  if (pollInterval) return
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

const { connection, isConnected, start } = useSignalR({
  hubUrl: `/hubs/leaderboard?competitionId=${props.competitionId}`,
  accessToken: () => auth.accessToken,
  onConnected: () => {
    stopPolling()
  },
  onDisconnected: () => {
    startPolling()
  },
  onReconnected: () => {
    stopPolling()
    fetchLeaderboard()
  },
})

watch(connection, (conn) => {
  if (!conn) return
  conn.on('ReceiveScoreUpdate', (data: { entries?: NoCtfapiEndpointsCompetitionsLeaderboardEntryDto[] }) => {
    if (data?.entries) {
      entries.value = data.entries
      scoreStore.updateFromLeaderboard(entries.value)
      emit('scoreUpdate', entries.value)
    }
  })
}, { immediate: true })

onMounted(async () => {
  await fetchLeaderboard()
  try {
    await start()
  } catch {
    startPolling()
  }
})

onUnmounted(() => {
  stopPolling()
})
</script>

<template>
  <div class="space-y-3">
    <div class="flex items-center justify-between">
      <h2 class="text-xl font-semibold">{{ t('scoreboard.title') }}</h2>
      <div class="flex items-center gap-2">
        <Badge v-if="isConnected" class="bg-green-600 text-white border-transparent text-xs">
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

    <div v-if="loading" class="text-sm text-muted-foreground">{{ t('scoreboard.loading') }}</div>

    <div v-else-if="entries.length === 0" class="text-sm text-muted-foreground">
      {{ t('scoreboard.empty') }}
    </div>

    <div v-else class="rounded-md border">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead class="w-12 text-center">{{ t('scoreboard.rank') }}</TableHead>
            <TableHead>{{ t('scoreboard.team') }}</TableHead>
            <TableHead class="text-right">{{ t('scoreboard.score') }}</TableHead>
            <TableHead class="text-right">{{ t('scoreboard.solves') }}</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          <TableRow
            v-for="entry in entries"
            :key="entry.teamId ?? entry.rank"
            class="transition-colors"
          >
            <TableCell class="text-center font-mono text-sm font-medium">
              {{ entry.rank }}
            </TableCell>
            <TableCell class="font-medium">{{ entry.teamName }}</TableCell>
            <TableCell class="text-right font-mono font-semibold">{{ entry.totalScore }}</TableCell>
            <TableCell class="text-right text-muted-foreground">{{ entry.solvedCount }}</TableCell>
          </TableRow>
        </TableBody>
      </Table>
    </div>
  </div>
</template>
