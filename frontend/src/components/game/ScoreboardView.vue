<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { competitionApi } from '@/api/noctf'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import HandDrawnLineChart from '@/components/ui/hand-drawn/HandDrawnLineChart.vue'
import HandDrawnUnderline from '@/components/ui/hand-drawn/HandDrawnUnderline.vue'
import type { HandDrawnSeries } from '@/components/ui/hand-drawn/HandDrawnLineChart.vue'
import { useSignalR } from '@/composables/useSignalR'
import { useAuthStore } from '@/stores/auth'
import { useScoreStore } from '@/stores/score'

interface LeaderboardEntry {
  rank?: number
  teamId?: string
  teamName?: string
  trackName?: string | null
  totalScore?: number
  score?: number
  solvedCount?: number
  firstSolveAt?: string | null
}

interface TrendPoint {
  timestamp: string
  score: number
}

interface TrendSeries {
  teamId: string
  teamName: string
  points: TrendPoint[]
}

interface TrendResponse {
  series?: TrendSeries[]
}

interface DirectionScore {
  direction: string
  score: number
  solvedCount: number
}

interface MemberSolve {
  challengeId: string
  challengeTitle: string
  direction: string
  submittedAt: string
}

interface MemberHistory {
  userId: string
  userName: string
  solves: MemberSolve[]
}

interface TeamDetail {
  teamId: string
  teamName: string
  trackName?: string | null
  totalScore: number
  solvedCount: number
  directionScores: DirectionScore[]
  challengeScores: ChallengeScore[]
  members: MemberHistory[]
}

interface ChallengeScore {
  challengeId: string
  challengeTitle: string
  direction: string
  currentPoints: number
  baseScore: number
  bonusScore: number
  totalScore: number
  bloodRank?: number | null
  solvedAt?: string | null
}

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
const trend = ref<TrendSeries[]>([])
const loading = ref(true)
const usingFallback = ref(false)
const selectedTeamId = ref<string | null>(null)
const selectedTeam = ref<TeamDetail | null>(null)
const detailLoading = ref(false)
const hoveredTeamId = ref<string | null>(null)
let pollInterval: ReturnType<typeof setInterval> | null = null

const markerColors = [
  '#E63946',
  '#1D3557',
  '#06D6A0',
  '#7B2CBF',
  '#F4A261',
  '#F72585',
  '#2A9D8F',
  '#E9C46A',
  '#457B9D',
  '#D62828',
]

const sortedEntries = computed(() => entries.value.map(entry => ({
  ...entry,
  totalScore: entry.totalScore ?? entry.score ?? 0,
  solvedCount: entry.solvedCount ?? 0,
})))

const displayedEntries = computed(() => {
  if (props.full === false)
    return sortedEntries.value.slice(0, 10)
  return sortedEntries.value
})

const trendSeries = computed<HandDrawnSeries[]>(() => {
  return trend.value
    .map((series, index) => ({
      id: series.teamId,
      name: series.teamName,
      color: markerColors[index % markerColors.length],
      points: (series.points ?? [])
        .map(point => ({
          x: Date.parse(point.timestamp),
          y: point.score ?? 0,
        }))
        .filter(point => Number.isFinite(point.x)),
    }))
    .filter(series => series.points.length > 0)
})

const trendBounds = computed(() => {
  const allPoints = trendSeries.value.flatMap(series => series.points)
  const xs = allPoints.map(point => point.x)
  const ys = allPoints.map(point => point.y)
  const minX = xs.length ? Math.min(...xs) : Date.now()
  const maxX = xs.length ? Math.max(...xs) : minX + 60_000
  const maxY = Math.max(1, ...ys)
  return { minX, maxX, maxY }
})

const scoreTicks = computed(() => {
  const ticks = 5
  return Array.from({ length: ticks }, (_, index) => {
    const value = Math.round((trendBounds.value.maxY * index) / (ticks - 1))
    return { value, label: String(value) }
  }).reverse()
})

const timeTicks = computed(() => {
  const { minX, maxX } = trendBounds.value
  const ticks = 4
  return Array.from({ length: ticks }, (_, index) => {
    const time = minX + ((maxX - minX) * index) / (ticks - 1)
    return {
      value: time,
      label: formatTimeTick(time),
    }
  })
})

function formatTimeTick(time: number) {
  return new Intl.DateTimeFormat(locale.value, {
    hour: '2-digit',
    minute: '2-digit',
  }).format(new Date(time))
}

function bloodLabel(rank?: number | null) {
  if (rank === 1)
    return t('scoreboard.firstBlood')
  if (rank === 2)
    return t('scoreboard.secondBlood')
  if (rank === 3)
    return t('scoreboard.thirdBlood')
  return ''
}

function formatChallengeScore(score: ChallengeScore) {
  if (!score.solvedAt)
    return '-'
  if (score.bonusScore === 0)
    return `${score.baseScore}`
  const sign = score.bonusScore > 0 ? '+' : ''
  return `${score.baseScore} (${sign}${score.bonusScore})`
}

function formatDate(value?: string | null) {
  if (!value)
    return '-'
  return new Intl.DateTimeFormat(locale.value, {
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  }).format(new Date(value))
}

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
  scoreStore.updateFromLeaderboard(entries.value as never, props.teamId, props.competitionId)
  emit('scoreUpdate', entries.value)
}

async function fetchLeaderboard() {
  try {
    const [leaderboardData, trendData] = await Promise.all([
      competitionApi.leaderboard(props.competitionId) as Promise<{ entries?: LeaderboardEntry[] }>,
      competitionApi.leaderboardTrend<TrendResponse>(props.competitionId),
    ])
    applyLeaderboard(leaderboardData?.entries ?? [])
    trend.value = trendData?.series ?? []
  }
  catch {
    // Keep the last successful snapshot while polling.
  }
  finally {
    loading.value = false
  }
}

async function openTeamDetail(teamId?: string) {
  if (!teamId)
    return
  selectedTeamId.value = teamId
  detailLoading.value = true
  selectedTeam.value = null
  try {
    selectedTeam.value = await competitionApi.leaderboardTeam<TeamDetail>(props.competitionId, teamId)
  }
  finally {
    detailLoading.value = false
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

const { connection, isConnected, start } = useSignalR({
  hubUrl: '/hubs/competition',
  accessToken: () => auth.accessToken,
  onConnected: () => {
    connection.value?.invoke('JoinCompetition', props.competitionId).catch(() => undefined)
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
  conn.on('ReceiveScoreUpdate', () => {
    fetchLeaderboard()
  })
}, { immediate: true })

watch(() => props.teamId, () => {
  scoreStore.updateFromLeaderboard(entries.value as never, props.teamId, props.competitionId)
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

    <Card class="relative overflow-hidden border-2 border-dashed border-[#D7CCC8] bg-[#FDFBF7] p-4 dark:border-[#4A4238] dark:bg-[#1C1917]">
      <div class="mb-3 flex items-center justify-between gap-3">
        <div>
          <h3 class="text-sm font-bold">
            {{ t('scoreboard.topTrend') }}
          </h3>
          <HandDrawnUnderline :width="80" color="#1D3557" />
        </div>
        <span class="text-xs text-muted-foreground">{{ t('scoreboard.topTrendHint') }}</span>
      </div>
      <HandDrawnLineChart
        :series="trendSeries"
        :x-ticks="timeTicks"
        :y-ticks="scoreTicks"
        :x-label="t('scoreboard.time')"
        :y-label="t('scoreboard.score')"
        :highlighted-id="hoveredTeamId"
        :empty="!trendSeries.length"
        :empty-label="t('scoreboard.empty')"
        @highlight="hoveredTeamId = $event"
      />
      <div class="mt-3 flex flex-wrap gap-2 text-xs">
        <span
          v-for="series in trendSeries"
          :key="series.id"
          class="inline-flex cursor-default items-center gap-1.5 rounded-full border border-dashed border-foreground/20 bg-background/60 px-2 py-1 transition-colors"
          :class="hoveredTeamId === series.id ? 'bg-muted text-foreground' : 'text-muted-foreground hover:bg-muted/60 hover:text-foreground'"
          @mouseenter="hoveredTeamId = series.id"
          @mouseleave="hoveredTeamId = null"
        >
          <span class="h-2.5 w-2.5 rounded-full" :style="{ backgroundColor: series.color }" />
          {{ series.name }}
        </span>
      </div>
    </Card>

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
          class="relative cursor-pointer border-2 border-dashed border-foreground/10 bg-[#FDFBF7] px-3 py-2 transition-all hover:-translate-y-0.5 hover:shadow-sm dark:bg-[#1C1917]"
          :class="rowTiltClass(index)"
          @mouseenter="hoveredTeamId = entry.teamId ?? null"
          @mouseleave="hoveredTeamId = null"
          @click="openTeamDetail(entry.teamId)"
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
              <div class="text-xs text-muted-foreground">
                {{ entry.trackName || '-' }}
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

    <Dialog :open="Boolean(selectedTeamId)" @update:open="(open) => { if (!open) selectedTeamId = null }">
      <DialogContent class="max-h-[85vh] max-w-3xl overflow-y-auto">
        <DialogHeader>
          <DialogTitle>{{ selectedTeam?.teamName ?? t('scoreboard.teamDetail') }}</DialogTitle>
        </DialogHeader>

        <div v-if="detailLoading" class="text-sm text-muted-foreground">
          {{ t('common.loading') }}
        </div>

        <div v-else-if="selectedTeam" class="space-y-5">
          <div class="grid gap-3 sm:grid-cols-3">
            <Card class="p-3 gap-2">
              <div class="text-xs text-muted-foreground">
                {{ t('scoreboard.score') }}
              </div>
              <div class="mt-1 font-mono text-2xl font-semibold">
                {{ selectedTeam.totalScore }}
              </div>
            </Card>
            <Card class="p-3 gap-2">
              <div class="text-xs text-muted-foreground">
                {{ t('scoreboard.solves') }}
              </div>
              <div class="mt-1 font-mono text-2xl font-semibold">
                {{ selectedTeam.solvedCount }}
              </div>
            </Card>
            <Card class="p-3 gap-2">
              <div class="text-xs text-muted-foreground">
                {{ t('scoreboard.track') }}
              </div>
              <div class="mt-1 text-lg font-semibold">
                {{ selectedTeam.trackName || '-' }}
              </div>
            </Card>
          </div>

          <div>
            <h3 class="mb-2 text-sm font-medium">
              {{ t('scoreboard.directionScores') }}
            </h3>
            <div v-if="selectedTeam.directionScores.length" class="grid gap-2 sm:grid-cols-2">
              <div
                v-for="direction in selectedTeam.directionScores"
                :key="direction.direction"
                class="flex items-center justify-between rounded-md border bg-background/60 px-3 py-2 text-sm"
              >
                <span class="font-medium">{{ direction.direction }}</span>
                <span class="font-mono">{{ direction.score }} / {{ direction.solvedCount }}</span>
              </div>
            </div>
            <p v-else class="text-sm text-muted-foreground">
              {{ t('scoreboard.noDirectionScores') }}
            </p>
          </div>

          <div>
            <h3 class="mb-2 text-sm font-medium">
              {{ t('scoreboard.challengeScores') }}
            </h3>
            <Card class="p-0 overflow-hidden">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>{{ t('challenges.title') }}</TableHead>
                    <TableHead>{{ t('admin.challenges.direction') }}</TableHead>
                    <TableHead class="text-right">
                      {{ t('scoreboard.currentPoints') }}
                    </TableHead>
                    <TableHead class="text-right">
                      {{ t('scoreboard.teamScore') }}
                    </TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  <TableRow v-for="score in selectedTeam.challengeScores" :key="score.challengeId">
                    <TableCell>
                      <div class="font-medium">
                        {{ score.challengeTitle }}
                      </div>
                      <Badge
                        v-if="score.bloodRank"
                        variant="outline"
                        class="mt-1 border-amber-500/70 bg-amber-500/10 text-amber-700"
                      >
                        {{ bloodLabel(score.bloodRank) }}
                      </Badge>
                    </TableCell>
                    <TableCell class="text-muted-foreground">
                      {{ score.direction }}
                    </TableCell>
                    <TableCell class="text-right font-mono">
                      {{ score.currentPoints }}
                    </TableCell>
                    <TableCell class="text-right font-mono" :class="score.solvedAt ? 'font-semibold text-foreground' : 'text-muted-foreground'">
                      {{ formatChallengeScore(score) }}
                    </TableCell>
                  </TableRow>
                </TableBody>
              </Table>
            </Card>
          </div>

          <div>
            <h3 class="mb-2 text-sm font-medium">
              {{ t('scoreboard.memberHistory') }}
            </h3>
            <div class="space-y-3">
              <div
                v-for="member in selectedTeam.members"
                :key="member.userId"
                class="rounded-lg border bg-background/60 p-3"
              >
                <div class="mb-2 flex items-center justify-between">
                  <span class="font-medium">{{ member.userName }}</span>
                  <Badge variant="secondary">
                    {{ member.solves.length }}
                  </Badge>
                </div>
                <div v-if="member.solves.length" class="space-y-1 text-sm">
                  <div
                    v-for="solve in member.solves"
                    :key="`${member.userId}-${solve.challengeId}-${solve.submittedAt}`"
                    class="flex flex-wrap items-center justify-between gap-2 text-muted-foreground"
                  >
                    <span>{{ solve.challengeTitle }} · {{ solve.direction }}</span>
                    <span class="font-mono text-xs">{{ formatDate(solve.submittedAt) }}</span>
                  </div>
                </div>
                <p v-else class="text-sm text-muted-foreground">
                  {{ t('scoreboard.noMemberSolves') }}
                </p>
              </div>
            </div>
          </div>

          <div class="flex justify-end">
            <Button variant="outline" @click="selectedTeamId = null">
              {{ t('common.close') }}
            </Button>
          </div>
        </div>
      </DialogContent>
    </Dialog>
  </div>
</template>
