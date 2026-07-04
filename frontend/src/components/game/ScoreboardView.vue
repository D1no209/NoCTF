<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { competitionApi } from '@/api/noctf'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogHeader, DialogTitle } from '@/components/ui/dialog'
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

interface TeamScoreRow {
  challengeId: string
  challengeTitle: string
  direction: string
  submitter: string
  score: string
  submittedAt?: string | null
  sortTime: number
  bloodRank?: number | null
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
  left: 68,
  right: 724,
  top: 38,
  bottom: 282,
}
const radarFrame = {
  centerX: 180,
  centerY: 150,
  radius: 98,
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
const sortedEntries = computed(() =>
  entries.value.map((entry) => ({
    ...entry,
    totalScore: entry.totalScore ?? entry.score ?? 0,
    solvedCount: entry.solvedCount ?? 0,
  })),
)

const visibleTrend = computed(() => {
  const activeTeamIds = new Set(
    sortedEntries.value
      .map((entry) => entry.teamId)
      .filter((teamId): teamId is string => Boolean(teamId)),
  )

  if (!activeTeamIds.size) return []
  return trend.value.filter((series) => activeTeamIds.has(series.teamId)).slice(0, 10)
})

const chartBounds = computed(() => {
  const allPoints = visibleTrend.value.flatMap((series) => series.points ?? [])
  const timestamps = allPoints
    .map((point) => Date.parse(point.timestamp))
    .filter((value) => Number.isFinite(value))
  const scores = allPoints.map((point) => point.score ?? 0)
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

const hoveredSeries = computed(
  () => visibleTrend.value.find((series) => series.teamId === hoveredTeamId.value) ?? null,
)
const hoveredSeriesIndex = computed(() =>
  visibleTrend.value.findIndex((series) => series.teamId === hoveredTeamId.value),
)
const hoveredSeriesPath = computed(() =>
  hoveredSeries.value ? buildPath(hoveredSeries.value.points) : '',
)
const hoveredSeriesColor = computed(
  () => chartColors[Math.max(0, hoveredSeriesIndex.value) % chartColors.length],
)

const selectedDirectionRadar = computed(() => {
  const directions = selectedTeam.value?.directionScores ?? []
  const axisCount = Math.max(directions.length, 3)
  const maxScore = niceScoreCeil(Math.max(...directions.map((direction) => direction.score), 1))
  const rings = [0.25, 0.5, 0.75, 1].map((ratio) => ({
    ratio,
    label: Math.round(maxScore * ratio),
    points: radarPolygonPoints(axisCount, radarFrame.radius * ratio),
  }))
  const axes = directions.map((direction, index) => {
    const outer = radarPoint(index, axisCount, radarFrame.radius)
    const label = radarPoint(index, axisCount, radarFrame.radius + 27)
    const value = radarPoint(
      index,
      axisCount,
      radarFrame.radius * (direction.score / Math.max(1, maxScore)),
    )
    return {
      ...direction,
      outer,
      label,
      value,
      textAnchor: radarTextAnchor(label.x),
      labelDy: radarLabelDy(label.y),
    }
  })
  const valuePoints = Array.from({ length: axisCount }, (_, index) => {
    const score = directions[index]?.score ?? 0
    return radarPoint(index, axisCount, radarFrame.radius * (score / Math.max(1, maxScore)))
  })
  return {
    axes,
    rings,
    maxScore,
    valuePoints: pointList(valuePoints),
  }
})

const selectedScoreRows = computed<TeamScoreRow[]>(() => {
  if (!selectedTeam.value) return []
  const challengeScores = new Map(
    selectedTeam.value.challengeScores.map((score) => [score.challengeId, score]),
  )
  const memberRows = selectedTeam.value.members.flatMap((member) =>
    member.solves.map((solve) => {
      const challengeScore = challengeScores.get(solve.challengeId)
      return {
        challengeId: solve.challengeId,
        challengeTitle: solve.challengeTitle,
        direction: solve.direction,
        submitter: member.userName,
        score: challengeScore ? formatChallengeScore(challengeScore) : '-',
        submittedAt: solve.submittedAt,
        sortTime: Date.parse(solve.submittedAt) || 0,
        bloodRank: challengeScore?.bloodRank,
      }
    }),
  )

  if (memberRows.length) {
    return memberRows.sort((first, second) => second.sortTime - first.sortTime)
  }

  return selectedTeam.value.challengeScores
    .filter((score) => score.solvedAt)
    .map((score) => ({
      challengeId: score.challengeId,
      challengeTitle: score.challengeTitle,
      direction: score.direction,
      submitter: '-',
      score: formatChallengeScore(score),
      submittedAt: score.solvedAt,
      sortTime: Date.parse(score.solvedAt ?? '') || 0,
      bloodRank: score.bloodRank,
    }))
    .sort((first, second) => second.sortTime - first.sortTime)
})

function niceScoreCeil(value: number) {
  if (value <= 10) return 10
  const magnitude = 10 ** Math.floor(Math.log10(value))
  const normalized = value / magnitude
  const nice = normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10
  return nice * magnitude
}

function radarPoint(index: number, count: number, radius: number) {
  const angle = -Math.PI / 2 + (Math.PI * 2 * index) / count
  return {
    x: radarFrame.centerX + Math.cos(angle) * radius,
    y: radarFrame.centerY + Math.sin(angle) * radius,
  }
}

function pointList(points: { x: number; y: number }[]) {
  return points.map((point) => `${point.x.toFixed(1)},${point.y.toFixed(1)}`).join(' ')
}

function radarPolygonPoints(count: number, radius: number) {
  return pointList(Array.from({ length: count }, (_, index) => radarPoint(index, count, radius)))
}

function radarTextAnchor(x: number) {
  if (x > radarFrame.centerX + 12) return 'start'
  if (x < radarFrame.centerX - 12) return 'end'
  return 'middle'
}

function radarLabelDy(y: number) {
  if (y < radarFrame.centerY - radarFrame.radius * 0.7) return '-0.45em'
  if (y > radarFrame.centerY + radarFrame.radius * 0.7) return '0.9em'
  return '0.35em'
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
    .map((point) => ({
      time: Date.parse(point.timestamp),
      score: point.score ?? 0,
    }))
    .filter((point) => Number.isFinite(point.time))
    .sort((a, b) => a.time - b.time)
}

function buildPath(points: TrendPoint[]) {
  if (!points?.length) return ''
  const mapped = normalizePoints(points).map((point) => ({
    x: timeToX(point.time),
    y: scoreToY(point.score),
  }))
  if (!mapped.length) return ''

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
    selectedTeam.value = await competitionApi.leaderboardTeam<TeamDetail>(
      props.competitionId,
      teamId,
    )
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

watch(
  connection,
  (conn) => {
    if (!conn) return
    conn.on('ReceiveLeaderboardSnapshot', (data: LeaderboardEntry[]) => {
      applyLeaderboard(data ?? [])
      competitionApi
        .leaderboardTrend<TrendResponse>(props.competitionId)
        .then((nextTrend) => {
          trend.value = nextTrend?.series ?? []
        })
        .catch(() => undefined)
    })
    conn.on('ReceiveScoreUpdate', () => {
      fetchLeaderboard()
    })
  },
  { immediate: true },
)

watch(
  () => props.teamId,
  () => {
    scoreStore.updateFromLeaderboard(entries.value as never, props.teamId, props.competitionId)
  },
)

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

    <div v-if="visibleTrend.length" class="rounded-lg border bg-background p-4">
      <div class="mb-3 flex items-center justify-between gap-3">
        <h3 class="text-sm font-medium">
          {{ t('scoreboard.topTrend') }}
        </h3>
        <span class="text-xs text-muted-foreground">{{ t('scoreboard.topTrendHint') }}</span>
      </div>
      <svg
        viewBox="0 0 760 328"
        class="h-80 w-full overflow-visible"
        @mouseleave="hoveredTeamId = null"
      >
        <rect
          :x="chartFrame.left"
          :y="chartFrame.top"
          :width="chartFrame.right - chartFrame.left"
          :height="chartFrame.bottom - chartFrame.top"
          rx="8"
          class="fill-muted/20"
        />
        <g class="text-[10px]">
          <g v-for="tick in scoreTicks" :key="`score-${tick.value}`">
            <line
              :x1="chartFrame.left"
              :y1="tick.y"
              :x2="chartFrame.right"
              :y2="tick.y"
              class="stroke-border/70"
              stroke-width="1"
              stroke-dasharray="2 7"
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
              :y1="chartFrame.top"
              :x2="tick.x"
              :y2="chartFrame.bottom"
              class="stroke-border/45"
              stroke-width="1"
              stroke-dasharray="2 9"
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
        <line
          :x1="chartFrame.left"
          :y1="chartFrame.bottom"
          :x2="chartFrame.right"
          :y2="chartFrame.bottom"
          class="stroke-border"
          stroke-width="1"
        />
        <line
          :x1="chartFrame.left"
          :y1="chartFrame.top"
          :x2="chartFrame.left"
          :y2="chartFrame.bottom"
          class="stroke-border"
          stroke-width="1"
        />
        <path
          v-for="(series, index) in visibleTrend"
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
        <g
          v-for="(series, index) in visibleTrend"
          :key="`end-${series.teamId}`"
          :style="{ opacity: seriesOpacity(series.teamId) }"
        >
          <circle
            v-if="normalizePoints(series.points).length"
            :cx="chartFrame.right"
            :cy="scoreToY(normalizePoints(series.points).at(-1)?.score ?? 0)"
            r="4"
            :fill="chartColors[index % chartColors.length]"
            class="stroke-background"
            stroke-width="2"
          />
          <text
            v-if="normalizePoints(series.points).length"
            :x="chartFrame.right + 10"
            :y="scoreToY(normalizePoints(series.points).at(-1)?.score ?? 0) + 4"
            class="fill-muted-foreground text-[10px] font-semibold"
          >
            {{ normalizePoints(series.points).at(-1)?.score ?? 0 }}
          </text>
        </g>
        <path
          v-for="series in visibleTrend"
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
          v-for="(series, index) in visibleTrend"
          :key="series.teamId"
          class="inline-flex cursor-default items-center gap-1.5 rounded-full px-2 py-1 transition-colors"
          :class="
            hoveredTeamId === series.teamId
              ? 'bg-muted text-foreground'
              : 'text-muted-foreground hover:bg-muted/60 hover:text-foreground'
          "
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

    <div v-if="loading" class="text-sm text-muted-foreground">
      {{ t('scoreboard.loading') }}
    </div>

    <div
      v-else-if="sortedEntries.length === 0"
      class="noctf-state-box text-sm text-muted-foreground"
    >
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

    <Dialog
      :open="Boolean(selectedTeamId)"
      @update:open="
        (open) => {
          if (!open) selectedTeamId = null
        }
      "
    >
      <DialogContent class="noctf-scrollbar max-h-[85vh] max-w-4xl overflow-y-auto">
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
            <div
              v-if="selectedDirectionRadar.axes.length"
              class="grid gap-4 rounded-lg border bg-background p-3 md:grid-cols-[minmax(0,22rem)_1fr] md:items-center"
            >
              <svg viewBox="0 0 360 300" class="h-72 w-full overflow-visible">
                <polygon
                  v-for="ring in selectedDirectionRadar.rings"
                  :key="ring.ratio"
                  :points="ring.points"
                  fill="none"
                  class="stroke-border/75"
                  stroke-width="1"
                />
                <line
                  v-for="axis in selectedDirectionRadar.axes"
                  :key="`axis-${axis.direction}`"
                  :x1="radarFrame.centerX"
                  :y1="radarFrame.centerY"
                  :x2="axis.outer.x"
                  :y2="axis.outer.y"
                  class="stroke-border/70"
                  stroke-width="1"
                  stroke-dasharray="3 6"
                />
                <polygon
                  :points="selectedDirectionRadar.valuePoints"
                  class="fill-primary/15 stroke-primary"
                  stroke-width="2"
                />
                <circle
                  v-for="axis in selectedDirectionRadar.axes"
                  :key="`point-${axis.direction}`"
                  :cx="axis.value.x"
                  :cy="axis.value.y"
                  r="4"
                  class="fill-primary stroke-background"
                  stroke-width="2"
                >
                  <title>{{ axis.direction }}: {{ axis.score }}</title>
                </circle>
                <text
                  v-for="axis in selectedDirectionRadar.axes"
                  :key="`score-${axis.direction}`"
                  :x="axis.value.x + 9"
                  :y="axis.value.y + 4"
                  class="fill-primary text-[11px] font-semibold tabular-nums"
                >
                  {{ axis.score }}
                </text>
                <text
                  v-for="axis in selectedDirectionRadar.axes"
                  :key="`label-${axis.direction}`"
                  :x="axis.label.x"
                  :y="axis.label.y"
                  :text-anchor="axis.textAnchor"
                  :dy="axis.labelDy"
                  class="fill-foreground text-[11px] font-semibold"
                >
                  {{ axis.direction }}
                </text>
              </svg>

              <div class="divide-y rounded-md border text-sm">
                <div
                  v-for="direction in selectedTeam.directionScores"
                  :key="direction.direction"
                  class="grid grid-cols-[minmax(0,1fr)_auto_auto] items-center gap-3 px-3 py-2"
                >
                  <span class="truncate font-medium">{{ direction.direction }}</span>
                  <span class="font-mono tabular-nums">{{ direction.score }}</span>
                  <span class="text-xs text-muted-foreground tabular-nums">
                    {{ direction.solvedCount }} {{ t('scoreboard.solves') }}
                  </span>
                </div>
              </div>
            </div>
            <p v-else class="text-sm text-muted-foreground">
              {{ t('scoreboard.noDirectionScores') }}
            </p>
          </div>

          <div>
            <h3 class="mb-2 text-sm font-medium">
              {{ t('scoreboard.scoreDetails') }}
            </h3>
            <div v-if="selectedScoreRows.length" class="noctf-table-shell">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>{{ t('challenges.title') }}</TableHead>
                    <TableHead>{{ t('admin.challenges.direction') }}</TableHead>
                    <TableHead>{{ t('scoreboard.submitter') }}</TableHead>
                    <TableHead class="text-right">
                      {{ t('scoreboard.score') }}
                    </TableHead>
                    <TableHead class="text-right">
                      {{ t('scoreboard.time') }}
                    </TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  <TableRow
                    v-for="row in selectedScoreRows"
                    :key="`${row.challengeId}-${row.submitter}-${row.submittedAt ?? 'unknown'}`"
                  >
                    <TableCell>
                      <div class="font-medium">
                        {{ row.challengeTitle }}
                      </div>
                      <Badge
                        v-if="row.bloodRank"
                        variant="outline"
                        class="mt-1 border-amber-500/70 bg-amber-500/10 text-amber-700"
                      >
                        {{ bloodLabel(row.bloodRank) }}
                      </Badge>
                    </TableCell>
                    <TableCell class="text-muted-foreground">
                      {{ row.direction }}
                    </TableCell>
                    <TableCell>
                      {{ row.submitter }}
                    </TableCell>
                    <TableCell class="text-right font-mono font-semibold">
                      {{ row.score }}
                    </TableCell>
                    <TableCell class="text-right font-mono text-xs text-muted-foreground">
                      {{ formatDate(row.submittedAt) }}
                    </TableCell>
                  </TableRow>
                </TableBody>
              </Table>
            </div>
            <p v-else class="text-sm text-muted-foreground">
              {{ t('scoreboard.noScoreDetails') }}
            </p>
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
