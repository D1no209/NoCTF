<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { competitionApi } from '@/api/noctf'
import { useAuthStore } from '@/stores/auth'
import { useScoreStore } from '@/stores/score'
import { useSignalR } from '@/composables/useSignalR'
import { Button } from '@/components/ui/button'
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
import { Badge } from '@/components/ui/badge'

type LeaderboardEntry = {
  rank?: number
  teamId?: string
  teamName?: string
  trackName?: string | null
  totalScore?: number
  score?: number
  solvedCount?: number
  firstSolveAt?: string | null
}

type TrendPoint = {
  timestamp: string
  score: number
}

type TrendSeries = {
  teamId: string
  teamName: string
  points: TrendPoint[]
}

type TrendResponse = {
  series?: TrendSeries[]
}

type DirectionScore = {
  direction: string
  score: number
  solvedCount: number
}

type MemberSolve = {
  challengeId: string
  challengeTitle: string
  direction: string
  submittedAt: string
}

type MemberHistory = {
  userId: string
  userName: string
  solves: MemberSolve[]
}

type TeamDetail = {
  teamId: string
  teamName: string
  trackName?: string | null
  totalScore: number
  solvedCount: number
  directionScores: DirectionScore[]
  challengeScores: ChallengeScore[]
  members: MemberHistory[]
}

type ChallengeScore = {
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
let pollInterval: ReturnType<typeof setInterval> | null = null

const chartColors = ['#2563eb', '#dc2626', '#16a34a', '#9333ea', '#ea580c', '#0891b2', '#be123c', '#4f46e5', '#65a30d', '#c2410c']

const sortedEntries = computed(() => entries.value.map((entry) => ({
  ...entry,
  totalScore: entry.totalScore ?? entry.score ?? 0,
  solvedCount: entry.solvedCount ?? 0,
})))

const chartBounds = computed(() => {
  const allPoints = trend.value.flatMap((series) => series.points ?? [])
  const timestamps = allPoints
    .map((point) => Date.parse(point.timestamp))
    .filter((value) => Number.isFinite(value))
  const scores = allPoints.map((point) => point.score ?? 0)
  const minTime = Math.min(...timestamps, Date.now())
  const maxTime = Math.max(...timestamps, minTime + 1)
  const maxScore = Math.max(...scores, 1)
  return { minTime, maxTime, maxScore }
})

function buildPath(points: TrendPoint[]) {
  if (!points?.length) return ''
  const { minTime, maxTime, maxScore } = chartBounds.value
  const mapped = points.map((point) => {
    const time = Date.parse(point.timestamp)
    const x = 36 + ((time - minTime) / Math.max(1, maxTime - minTime)) * 588
    const y = 212 - ((point.score ?? 0) / Math.max(1, maxScore)) * 176
    return { x, y }
  })
  return mapped.reduce((path, point, index) => {
    if (index === 0) return `M ${point.x.toFixed(1)} ${point.y.toFixed(1)}`
    const previous = mapped[index - 1]
    return `${path} L ${point.x.toFixed(1)} ${previous.y.toFixed(1)} L ${point.x.toFixed(1)} ${point.y.toFixed(1)}`
  }, '')
}

function bloodLabel(rank?: number | null) {
  if (rank === 1) return t('scoreboard.firstBlood')
  if (rank === 2) return t('scoreboard.secondBlood')
  if (rank === 3) return t('scoreboard.thirdBlood')
  return ''
}

function formatChallengeScore(score: ChallengeScore) {
  if (!score.solvedAt) return '-'
  if (score.bonusScore === 0) return `${score.baseScore}`
  const sign = score.bonusScore > 0 ? '+' : ''
  return `${score.baseScore} (${sign}${score.bonusScore})`
}

function formatDate(value?: string | null) {
  if (!value) return '-'
  return new Intl.DateTimeFormat(locale.value, {
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  }).format(new Date(value))
}

function applyLeaderboard(nextEntries: LeaderboardEntry[]) {
  entries.value = nextEntries
  scoreStore.updateFromLeaderboard(entries.value as never, props.teamId)
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
  } catch {
    // Keep the last successful snapshot while polling.
  } finally {
    loading.value = false
  }
}

async function openTeamDetail(teamId?: string) {
  if (!teamId) return
  selectedTeamId.value = teamId
  detailLoading.value = true
  selectedTeam.value = null
  try {
    selectedTeam.value = await competitionApi.leaderboardTeam<TeamDetail>(props.competitionId, teamId)
  } finally {
    detailLoading.value = false
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
  conn.on('ReceiveLeaderboardSnapshot', (data: LeaderboardEntry[]) => {
    applyLeaderboard(data ?? [])
    competitionApi.leaderboardTrend<TrendResponse>(props.competitionId)
      .then((nextTrend) => {
        trend.value = nextTrend?.series ?? []
      })
      .catch(() => undefined)
  })
  conn.on('ReceiveScoreUpdate', () => {
    fetchLeaderboard()
  })
}, { immediate: true })

watch(() => props.teamId, () => {
  scoreStore.updateFromLeaderboard(entries.value as never, props.teamId)
})

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
  <div class="space-y-4">
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

    <div v-if="trend.length" class="rounded-md border p-3">
      <div class="mb-2 flex items-center justify-between gap-3">
        <h3 class="text-sm font-medium">{{ t('scoreboard.topTrend') }}</h3>
        <span class="text-xs text-muted-foreground">{{ t('scoreboard.topTrendHint') }}</span>
      </div>
      <svg viewBox="0 0 660 240" class="h-56 w-full overflow-visible">
        <line x1="36" y1="212" x2="624" y2="212" class="stroke-border" stroke-width="1" />
        <line x1="36" y1="36" x2="36" y2="212" class="stroke-border" stroke-width="1" />
        <path
          v-for="(series, index) in trend"
          :key="series.teamId"
          :d="buildPath(series.points)"
          fill="none"
          :stroke="chartColors[index % chartColors.length]"
          stroke-width="2.5"
          stroke-linecap="round"
          stroke-linejoin="round"
        />
      </svg>
      <div class="flex flex-wrap gap-3 text-xs">
        <span
          v-for="(series, index) in trend"
          :key="series.teamId"
          class="inline-flex items-center gap-1.5"
        >
          <span
            class="h-2.5 w-2.5 rounded-full"
            :style="{ backgroundColor: chartColors[index % chartColors.length] }"
          />
          {{ series.teamName }}
        </span>
      </div>
    </div>

    <div v-if="loading" class="text-sm text-muted-foreground">{{ t('scoreboard.loading') }}</div>

    <div v-else-if="sortedEntries.length === 0" class="text-sm text-muted-foreground">
      {{ t('scoreboard.empty') }}
    </div>

    <div v-else class="rounded-md border">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead class="w-12 text-center">{{ t('scoreboard.rank') }}</TableHead>
            <TableHead>{{ t('scoreboard.team') }}</TableHead>
            <TableHead>{{ t('scoreboard.track') }}</TableHead>
            <TableHead class="text-right">{{ t('scoreboard.score') }}</TableHead>
            <TableHead class="text-right">{{ t('scoreboard.solves') }}</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          <TableRow
            v-for="entry in sortedEntries"
            :key="entry.teamId ?? entry.rank"
            class="cursor-pointer transition-colors hover:bg-muted/60"
            @click="openTeamDetail(entry.teamId)"
          >
            <TableCell class="text-center font-mono text-sm font-medium">
              {{ entry.rank }}
            </TableCell>
            <TableCell class="font-medium">{{ entry.teamName }}</TableCell>
            <TableCell class="text-muted-foreground">{{ entry.trackName || '-' }}</TableCell>
            <TableCell class="text-right font-mono font-semibold">{{ entry.totalScore }}</TableCell>
            <TableCell class="text-right text-muted-foreground">{{ entry.solvedCount }}</TableCell>
          </TableRow>
        </TableBody>
      </Table>
    </div>

    <Dialog :open="Boolean(selectedTeamId)" @update:open="(open) => { if (!open) selectedTeamId = null }">
      <DialogContent class="max-w-3xl">
        <DialogHeader>
          <DialogTitle>{{ selectedTeam?.teamName ?? t('scoreboard.teamDetail') }}</DialogTitle>
        </DialogHeader>

        <div v-if="detailLoading" class="text-sm text-muted-foreground">
          {{ t('common.loading') }}
        </div>

        <div v-else-if="selectedTeam" class="space-y-5">
          <div class="grid gap-3 sm:grid-cols-3">
            <div class="rounded-md border p-3">
              <div class="text-xs text-muted-foreground">{{ t('scoreboard.score') }}</div>
              <div class="mt-1 font-mono text-2xl font-semibold">{{ selectedTeam.totalScore }}</div>
            </div>
            <div class="rounded-md border p-3">
              <div class="text-xs text-muted-foreground">{{ t('scoreboard.solves') }}</div>
              <div class="mt-1 font-mono text-2xl font-semibold">{{ selectedTeam.solvedCount }}</div>
            </div>
            <div class="rounded-md border p-3">
              <div class="text-xs text-muted-foreground">{{ t('scoreboard.track') }}</div>
              <div class="mt-1 text-lg font-semibold">{{ selectedTeam.trackName || '-' }}</div>
            </div>
          </div>

          <div>
            <h3 class="mb-2 text-sm font-medium">{{ t('scoreboard.directionScores') }}</h3>
            <div v-if="selectedTeam.directionScores.length" class="grid gap-2 sm:grid-cols-2">
              <div
                v-for="direction in selectedTeam.directionScores"
                :key="direction.direction"
                class="flex items-center justify-between rounded-md border px-3 py-2 text-sm"
              >
                <span class="font-medium">{{ direction.direction }}</span>
                <span class="font-mono">{{ direction.score }} / {{ direction.solvedCount }}</span>
              </div>
            </div>
            <p v-else class="text-sm text-muted-foreground">{{ t('scoreboard.noDirectionScores') }}</p>
          </div>

          <div>
            <h3 class="mb-2 text-sm font-medium">{{ t('scoreboard.challengeScores') }}</h3>
            <div class="rounded-md border">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>{{ t('challenges.title') }}</TableHead>
                    <TableHead>{{ t('admin.challenges.direction') }}</TableHead>
                    <TableHead class="text-right">{{ t('scoreboard.currentPoints') }}</TableHead>
                    <TableHead class="text-right">{{ t('scoreboard.teamScore') }}</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  <TableRow v-for="score in selectedTeam.challengeScores" :key="score.challengeId">
                    <TableCell>
                      <div class="font-medium">{{ score.challengeTitle }}</div>
                      <Badge
                        v-if="score.bloodRank"
                        variant="outline"
                        class="mt-1 border-amber-500/70 bg-amber-500/10 text-amber-700"
                      >
                        {{ bloodLabel(score.bloodRank) }}
                      </Badge>
                    </TableCell>
                    <TableCell class="text-muted-foreground">{{ score.direction }}</TableCell>
                    <TableCell class="text-right font-mono">{{ score.currentPoints }}</TableCell>
                    <TableCell class="text-right font-mono" :class="score.solvedAt ? 'font-semibold text-foreground' : 'text-muted-foreground'">
                      {{ formatChallengeScore(score) }}
                    </TableCell>
                  </TableRow>
                </TableBody>
              </Table>
            </div>
          </div>

          <div>
            <h3 class="mb-2 text-sm font-medium">{{ t('scoreboard.memberHistory') }}</h3>
            <div class="space-y-3">
              <div
                v-for="member in selectedTeam.members"
                :key="member.userId"
                class="rounded-md border p-3"
              >
                <div class="mb-2 flex items-center justify-between">
                  <span class="font-medium">{{ member.userName }}</span>
                  <Badge variant="secondary">{{ member.solves.length }}</Badge>
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
                <p v-else class="text-sm text-muted-foreground">{{ t('scoreboard.noMemberSolves') }}</p>
              </div>
            </div>
          </div>

          <div class="flex justify-end">
            <Button variant="outline" @click="selectedTeamId = null">{{ t('common.close') }}</Button>
          </div>
        </div>
      </DialogContent>
    </Dialog>
  </div>
</template>
