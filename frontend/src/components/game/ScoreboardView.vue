<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { competitionApi } from '@/api/noctf'
import { Badge } from '@/components/ui/badge'
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

const chartFrame = {
  left: 58,
  right: 624,
  top: 32,
  bottom: 198,
}
const chartColors = [
  'oklch(0.56 0.23 262)',
  'oklch(0.61 0.22 28)',
  'oklch(0.58 0.16 150)',
  'oklch(0.58 0.20 305)',
  'oklch(0.66 0.19 55)',
  'oklch(0.60 0.13 205)',
  'oklch(0.55 0.20 15)',
  'oklch(0.52 0.16 240)',
  'oklch(0.62 0.14 125)',
  'oklch(0.58 0.17 75)',
]
const leaderboardFrame = {
  left: 78,
  right: 624,
  top: 30,
  bottom: 248,
}
const leaderboardRowHeight = 20

const sortedEntries = computed(() => entries.value.map(entry => ({
  ...entry,
  totalScore: entry.totalScore ?? entry.score ?? 0,
  solvedCount: entry.solvedCount ?? 0,
})))

const leaderboardChartEntries = computed(() => sortedEntries.value.slice(0, 10))

const leaderboardBounds = computed(() => {
  const scores = leaderboardChartEntries.value.map(entry => entry.totalScore ?? 0)
  const maxScore = niceScoreCeil(Math.max(...scores, 1))
  const minScore = Math.min(0, ...scores)
  return {
    minScore: minScore < 0 ? -niceScoreCeil(Math.abs(minScore)) : 0,
    maxScore,
  }
})

const leaderboardScoreTicks = computed(() => {
  const { minScore, maxScore } = leaderboardBounds.value
  const range = Math.max(1, maxScore - minScore)
  const ticks = 5
  const values = new Set<number>()
  for (let index = 0; index < ticks; index++)
    values.add(Math.round(minScore + (range * index) / (ticks - 1)))
  values.add(0)
  return Array.from(values).sort((first, second) => first - second)
})

const leaderboardChartRows = computed(() => leaderboardChartEntries.value.map((entry, index) => {
  const score = entry.totalScore ?? 0
  const zeroX = leaderboardScoreToX(0)
  const scoreX = leaderboardScoreToX(score)
  return {
    entry,
    y: leaderboardFrame.top + index * leaderboardRowHeight,
    x: Math.min(zeroX, scoreX),
    width: Math.max(2, Math.abs(scoreX - zeroX)),
    scoreX,
    scoreLabelX: score >= 0
      ? Math.min(leaderboardFrame.right - 8, scoreX + 8)
      : Math.max(leaderboardFrame.left + 8, scoreX - 8),
    scoreLabelAnchor: score >= 0 ? 'start' : 'end',
  }
}))

const chartBounds = computed(() => {
  const allPoints = trend.value.flatMap(series => series.points ?? [])
  const timestamps = allPoints
    .map(point => Date.parse(point.timestamp))
    .filter(value => Number.isFinite(value))
  const scores = allPoints.map(point => point.score ?? 0)
  const now = Date.now()
  const minTime = timestamps.length ? Math.min(...timestamps) : now
  const rawMaxTime = timestamps.length ? Math.max(...timestamps) : now + 60_000
  const maxTime = rawMaxTime <= minTime ? minTime + 60_000 : rawMaxTime
  const maxScore = niceScoreCeil(Math.max(...scores, 1))
  return { minTime, maxTime, maxScore }
})

const scoreTicks = computed(() => {
  const ticks = 5
  return Array.from({ length: ticks }, (_, index) => {
    const value = Math.round((chartBounds.value.maxScore * index) / (ticks - 1))
    return {
      value,
      y: scoreToY(value),
    }
  }).reverse()
})

const timeTicks = computed(() => {
  const { minTime, maxTime } = chartBounds.value
  const ticks = 4
  return Array.from({ length: ticks }, (_, index) => {
    const time = minTime + ((maxTime - minTime) * index) / (ticks - 1)
    return {
      timestamp: time,
      x: timeToX(time),
      label: formatTimeTick(time),
    }
  })
})

const hoveredSeries = computed(() => trend.value.find(series => series.teamId === hoveredTeamId.value) ?? null)
const hoveredSeriesIndex = computed(() => trend.value.findIndex(series => series.teamId === hoveredTeamId.value))
const hoveredSeriesPath = computed(() => hoveredSeries.value ? buildPath(hoveredSeries.value.points) : '')
const hoveredSeriesColor = computed(() => chartColors[Math.max(0, hoveredSeriesIndex.value) % chartColors.length])

function niceScoreCeil(value: number) {
  if (value <= 10)
    return 10
  const magnitude = 10 ** Math.floor(Math.log10(value))
  const normalized = value / magnitude
  const nice = normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10
  return nice * magnitude
}

function leaderboardScoreToX(score: number) {
  const width = leaderboardFrame.right - leaderboardFrame.left
  const { minScore, maxScore } = leaderboardBounds.value
  return leaderboardFrame.left + ((score - minScore) / Math.max(1, maxScore - minScore)) * width
}

function shortTeamName(name?: string) {
  if (!name)
    return '-'
  return name.length > 18 ? `${name.slice(0, 17)}...` : name
}

function scoreToY(score: number) {
  const height = chartFrame.bottom - chartFrame.top
  return chartFrame.bottom - (score / Math.max(1, chartBounds.value.maxScore)) * height
}

function timeToX(time: number) {
  const width = chartFrame.right - chartFrame.left
  const { minTime, maxTime } = chartBounds.value
  return chartFrame.left + ((time - minTime) / Math.max(1, maxTime - minTime)) * width
}

function normalizePoints(points: TrendPoint[]) {
  return (points ?? [])
    .map(point => ({
      time: Date.parse(point.timestamp),
      score: point.score ?? 0,
    }))
    .filter(point => Number.isFinite(point.time))
    .sort((a, b) => a.time - b.time)
}

function buildPath(points: TrendPoint[]) {
  if (!points?.length)
    return ''
  const mapped = normalizePoints(points).map(point => ({
    x: timeToX(point.time),
    y: scoreToY(point.score),
  }))
  if (!mapped.length)
    return ''

  let path = `M ${chartFrame.left} ${mapped[0].y.toFixed(1)} L ${mapped[0].x.toFixed(1)} ${mapped[0].y.toFixed(1)}`
  for (let index = 1; index < mapped.length; index += 1) {
    const previous = mapped[index - 1]
    const point = mapped[index]
    path += ` L ${point.x.toFixed(1)} ${previous.y.toFixed(1)} L ${point.x.toFixed(1)} ${point.y.toFixed(1)}`
  }

  const last = mapped[mapped.length - 1]
  path += ` L ${chartFrame.right} ${last.y.toFixed(1)}`
  return path
}

function isSeriesHighlighted(teamId: string) {
  return !hoveredTeamId.value || hoveredTeamId.value === teamId
}

function seriesStrokeWidth(teamId: string) {
  return hoveredTeamId.value === teamId ? 4 : 2.25
}

function seriesOpacity(teamId: string) {
  return isSeriesHighlighted(teamId) ? 1 : 0.22
}

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
  if (!conn)
    return
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
  <div class="space-y-4">
    <div class="flex items-center justify-between">
      <h2 class="text-xl font-semibold">
        {{ t('scoreboard.title') }}
      </h2>
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

    <div v-if="trend.length" class="rounded-xl border bg-background/55 p-3">
      <div class="mb-2 flex items-center justify-between gap-3">
        <h3 class="text-sm font-medium">
          {{ t('scoreboard.topTrend') }}
        </h3>
        <span class="text-xs text-muted-foreground">{{ t('scoreboard.topTrendHint') }}</span>
      </div>
      <svg viewBox="0 0 660 240" class="h-56 w-full overflow-visible" @mouseleave="hoveredTeamId = null">
        <g class="text-[10px]">
          <g v-for="tick in scoreTicks" :key="`score-${tick.value}`">
            <line
              :x1="chartFrame.left"
              :y1="tick.y"
              :x2="chartFrame.right"
              :y2="tick.y"
              class="stroke-border/80"
              stroke-width="1"
              stroke-dasharray="3 5"
            />
            <text
              :x="chartFrame.left - 10"
              :y="tick.y + 3"
              text-anchor="end"
              class="fill-muted-foreground"
            >
              {{ tick.value }}
            </text>
          </g>
          <g v-for="tick in timeTicks" :key="`time-${tick.timestamp}`">
            <line
              :x1="tick.x"
              :y1="chartFrame.bottom"
              :x2="tick.x"
              :y2="chartFrame.bottom + 4"
              class="stroke-border"
              stroke-width="1"
            />
            <text
              :x="tick.x"
              :y="chartFrame.bottom + 19"
              text-anchor="middle"
              class="fill-muted-foreground"
            >
              {{ tick.label }}
            </text>
          </g>
        </g>
        <text
          :x="chartFrame.left - 38"
          :y="chartFrame.top - 10"
          class="fill-muted-foreground text-[10px] font-medium"
        >
          {{ t('scoreboard.score') }}
        </text>
        <text
          :x="chartFrame.right"
          :y="chartFrame.bottom + 33"
          text-anchor="end"
          class="fill-muted-foreground text-[10px] font-medium"
        >
          {{ t('scoreboard.time') }}
        </text>
        <line :x1="chartFrame.left" :y1="chartFrame.bottom" :x2="chartFrame.right" :y2="chartFrame.bottom" class="stroke-border" stroke-width="1" />
        <line :x1="chartFrame.left" :y1="chartFrame.top" :x2="chartFrame.left" :y2="chartFrame.bottom" class="stroke-border" stroke-width="1" />
        <path
          v-for="(series, index) in trend"
          :key="series.teamId"
          :d="buildPath(series.points)"
          fill="none"
          :stroke="chartColors[index % chartColors.length]"
          :stroke-width="seriesStrokeWidth(series.teamId)"
          stroke-linecap="round"
          stroke-linejoin="round"
          class="scoreboard-trend-line"
          :style="{ opacity: seriesOpacity(series.teamId) }"
        />
        <path
          v-for="series in trend"
          :key="`hit-${series.teamId}`"
          :d="buildPath(series.points)"
          fill="none"
          stroke="transparent"
          stroke-width="16"
          stroke-linecap="round"
          stroke-linejoin="round"
          class="cursor-pointer"
          @mouseenter="hoveredTeamId = series.teamId"
          @focus="hoveredTeamId = series.teamId"
        />
        <circle
          v-if="hoveredSeriesPath"
          :key="hoveredTeamId ?? 'particle'"
          r="4"
          :fill="hoveredSeriesColor"
          class="scoreboard-trend-particle"
        >
          <animateMotion
            :path="hoveredSeriesPath"
            dur="850ms"
            begin="0s"
            fill="freeze"
            repeatCount="1"
          />
        </circle>
        <path
          v-if="hoveredSeriesPath"
          :d="hoveredSeriesPath"
          fill="none"
          :stroke="hoveredSeriesColor"
          stroke-width="6"
          stroke-linecap="round"
          stroke-linejoin="round"
          class="scoreboard-trend-glow"
        />
      </svg>
      <div class="flex flex-wrap gap-3 text-xs">
        <span
          v-for="(series, index) in trend"
          :key="series.teamId"
          class="inline-flex cursor-default items-center gap-1.5 rounded-full px-2 py-1 transition-colors"
          :class="hoveredTeamId === series.teamId ? 'bg-muted text-foreground' : 'text-muted-foreground hover:bg-muted/60 hover:text-foreground'"
          @mouseenter="hoveredTeamId = series.teamId"
          @mouseleave="hoveredTeamId = null"
        >
          <span
            class="h-2.5 w-2.5 rounded-full"
            :style="{ backgroundColor: chartColors[index % chartColors.length] }"
          />
          {{ series.teamName }}
        </span>
      </div>
    </div>

    <div v-if="leaderboardChartEntries.length" class="rounded-xl border bg-background/55 p-3">
      <div class="mb-2 flex items-center justify-between gap-3">
        <h3 class="text-sm font-medium">
          {{ t('scoreboard.scoreDistribution') }}
        </h3>
        <span class="text-xs text-muted-foreground">{{ t('scoreboard.scoreDistributionHint') }}</span>
      </div>
      <svg viewBox="0 0 660 292" class="h-72 w-full overflow-visible">
        <g class="text-[10px]">
          <g v-for="tick in leaderboardScoreTicks" :key="`leaderboard-score-${tick}`">
            <line
              :x1="leaderboardScoreToX(tick)"
              :y1="leaderboardFrame.top - 10"
              :x2="leaderboardScoreToX(tick)"
              :y2="leaderboardFrame.bottom"
              class="stroke-border/80"
              stroke-width="1"
              stroke-dasharray="3 6"
            />
            <text
              :x="leaderboardScoreToX(tick)"
              :y="leaderboardFrame.bottom + 17"
              text-anchor="middle"
              class="fill-muted-foreground tabular-nums"
            >
              {{ tick }}
            </text>
          </g>
          <line
            v-if="leaderboardBounds.minScore < 0"
            :x1="leaderboardScoreToX(0)"
            :y1="leaderboardFrame.top - 10"
            :x2="leaderboardScoreToX(0)"
            :y2="leaderboardFrame.bottom"
            class="stroke-foreground/35"
            stroke-width="1.5"
          />
          <line :x1="leaderboardFrame.left" :y1="leaderboardFrame.bottom" :x2="leaderboardFrame.right" :y2="leaderboardFrame.bottom" class="stroke-border" stroke-width="1" />
          <line :x1="leaderboardFrame.left" :y1="leaderboardFrame.top - 10" :x2="leaderboardFrame.left" :y2="leaderboardFrame.bottom" class="stroke-border" stroke-width="1" />
        </g>

        <text :x="leaderboardFrame.left - 54" :y="leaderboardFrame.top - 15" class="fill-muted-foreground text-[10px] font-medium">
          {{ t('scoreboard.rank') }}
        </text>
        <text :x="leaderboardFrame.right" :y="leaderboardFrame.bottom + 34" text-anchor="end" class="fill-muted-foreground text-[10px] font-medium">
          {{ t('scoreboard.score') }}
        </text>

        <g v-for="row in leaderboardChartRows" :key="row.entry.teamId ?? row.entry.teamName">
          <line
            :x1="leaderboardFrame.left"
            :y1="row.y + 8"
            :x2="leaderboardFrame.right"
            :y2="row.y + 8"
            class="stroke-border/55"
            stroke-width="1"
            stroke-dasharray="2 8"
          />
          <text
            :x="leaderboardFrame.left - 13"
            :y="row.y + 12"
            text-anchor="end"
            class="fill-muted-foreground text-[10px] font-semibold tabular-nums"
          >
            {{ row.entry.rank ?? '-' }}
          </text>
          <text
            :x="leaderboardFrame.left + 4"
            :y="row.y + 25"
            class="fill-muted-foreground text-[10px]"
          >
            {{ shortTeamName(row.entry.teamName) }}
          </text>
          <rect
            :x="row.x"
            :y="row.y"
            :width="row.width"
            height="14"
            rx="4"
            :class="(row.entry.rank ?? 99) <= 3 ? 'fill-primary/80' : 'fill-primary/50'"
          />
          <circle :cx="row.scoreX" :cy="row.y + 7" r="3" class="fill-primary stroke-background" stroke-width="1.5" />
          <text
            :x="row.scoreLabelX"
            :y="row.y + 12"
            :text-anchor="row.scoreLabelAnchor"
            class="fill-foreground text-[10px] font-semibold tabular-nums"
          >
            {{ row.entry.totalScore }}
          </text>
        </g>
      </svg>
    </div>

    <div v-if="loading" class="text-sm text-muted-foreground">
      {{ t('scoreboard.loading') }}
    </div>

    <div v-else-if="sortedEntries.length === 0" class="noctf-state-box text-sm text-muted-foreground">
      {{ t('scoreboard.empty') }}
    </div>

    <div v-else class="noctf-table-shell">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead class="w-12 text-center">
              {{ t('scoreboard.rank') }}
            </TableHead>
            <TableHead>{{ t('scoreboard.team') }}</TableHead>
            <TableHead>{{ t('scoreboard.track') }}</TableHead>
            <TableHead class="text-right">
              {{ t('scoreboard.score') }}
            </TableHead>
            <TableHead class="text-right">
              {{ t('scoreboard.solves') }}
            </TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          <TableRow
            v-for="entry in sortedEntries"
            :key="entry.teamId ?? entry.rank"
            class="cursor-pointer transition-colors hover:bg-muted/60"
            :class="entry.teamId && hoveredTeamId === entry.teamId ? 'bg-muted/70' : ''"
            @mouseenter="hoveredTeamId = entry.teamId ?? null"
            @mouseleave="hoveredTeamId = null"
            @click="openTeamDetail(entry.teamId)"
          >
            <TableCell class="text-center font-mono text-sm font-medium">
              {{ entry.rank }}
            </TableCell>
            <TableCell class="font-medium">
              {{ entry.teamName }}
            </TableCell>
            <TableCell class="text-muted-foreground">
              {{ entry.trackName || '-' }}
            </TableCell>
            <TableCell class="text-right font-mono font-semibold">
              {{ entry.totalScore }}
            </TableCell>
            <TableCell class="text-right text-muted-foreground">
              {{ entry.solvedCount }}
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>
    </div>

    <Dialog :open="Boolean(selectedTeamId)" @update:open="(open) => { if (!open) selectedTeamId = null }">
      <DialogContent class="noctf-scrollbar max-h-[85vh] max-w-3xl overflow-y-auto">
        <DialogHeader>
          <DialogTitle>{{ selectedTeam?.teamName ?? t('scoreboard.teamDetail') }}</DialogTitle>
        </DialogHeader>

        <div v-if="detailLoading" class="text-sm text-muted-foreground">
          {{ t('common.loading') }}
        </div>

        <div v-else-if="selectedTeam" class="space-y-5">
          <div class="grid gap-3 sm:grid-cols-3">
            <div class="noctf-kpi">
              <div class="text-xs text-muted-foreground">
                {{ t('scoreboard.score') }}
              </div>
              <div class="mt-1 font-mono text-2xl font-semibold">
                {{ selectedTeam.totalScore }}
              </div>
            </div>
            <div class="noctf-kpi">
              <div class="text-xs text-muted-foreground">
                {{ t('scoreboard.solves') }}
              </div>
              <div class="mt-1 font-mono text-2xl font-semibold">
                {{ selectedTeam.solvedCount }}
              </div>
            </div>
            <div class="noctf-kpi">
              <div class="text-xs text-muted-foreground">
                {{ t('scoreboard.track') }}
              </div>
              <div class="mt-1 text-lg font-semibold">
                {{ selectedTeam.trackName || '-' }}
              </div>
            </div>
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
            <div class="noctf-table-shell">
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
            </div>
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
                    <span>{{ solve.challengeTitle }} / {{ solve.direction }}</span>
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

<style scoped>
.scoreboard-trend-line {
  transition:
    opacity 180ms cubic-bezier(0.16, 1, 0.3, 1),
    stroke-width 180ms cubic-bezier(0.16, 1, 0.3, 1);
}

.scoreboard-trend-glow {
  opacity: 0.16;
  pointer-events: none;
  filter: drop-shadow(0 0 10px currentColor);
}

.scoreboard-trend-particle {
  pointer-events: none;
  filter: drop-shadow(0 0 8px currentColor);
}
</style>
