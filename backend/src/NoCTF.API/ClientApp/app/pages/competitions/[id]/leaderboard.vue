<script setup lang="ts">
import {
  Download,
  Medal,
  Trophy,
} from '@lucide/vue'
import { getLeaderboardEndpoint } from '~/api'
import type {
  NoCtfapiEndpointsCompetitionsLeaderboardProtocolResponse,
} from '~/api'
import { cn } from '@/lib/utils'
import { bloodRankLabel, medalBloodRankClass, medalRankClass, normalizeChallengeKey } from '~/components/leaderboard/types'
import type { ChallengeInfo, LeaderboardCell, MatrixEntry, TrendSeries } from '~/components/leaderboard/types'

const route = useRoute()
const competitionId = route.params.id as string
const ctx = inject(competitionContextKey)!

const leaderboard = ref<NoCtfapiEndpointsCompetitionsLeaderboardProtocolResponse | null>(null)
const loading = ref(true)
const error = ref<string | null>(null)

/** 拉取一次;返回 true 表示拿到 200 数据(不再需要重试)。 */
async function fetchOnce(): Promise<boolean> {
  const { data, error: err, response } = await getLeaderboardEndpoint({ path: { competitionId } })
  loading.value = false
  if (err) {
    error.value = parseApiError(err, translate("加载记分板失败")).message
    return true
  }
  if (response?.status === 202) return false
  error.value = null
  leaderboard.value = data as NoCtfapiEndpointsCompetitionsLeaderboardProtocolResponse
  return true
}

const { polling, start: startPolling } = usePolling(fetchOnce, { interval: 2000, timeout: 60_000 })

onMounted(async () => {
  const done = await fetchOnce()
  if (!done) startPolling()
})

// 实时:排行榜投影刷新 → 重新拉取
let unwatch: (() => void) | undefined
onMounted(() => {
  unwatch = watchCompetition(competitionId, {
    leaderboardRefreshed: () => void fetchOnce(),
  })
})
onUnmounted(() => unwatch?.())

// ---- 题目矩阵列 ----
const challenges = computed<ChallengeInfo[]>(
  () => (leaderboard.value?.challenges ?? []) as ChallengeInfo[],
)

interface DirectionGroup {
  direction: string
  challenges: ChallengeInfo[]
}

const directionGroups = computed<DirectionGroup[]>(() => {
  const groups: DirectionGroup[] = []
  for (const challenge of challenges.value) {
    const direction = challenge.direction || translate('未分类')
    const last = groups[groups.length - 1]
    if (last && last.direction === direction) last.challenges.push(challenge)
    else groups.push({ direction, challenges: [challenge] })
  }
  return groups
})

const showGroupLabels = ref(true)

// ---- 队伍 × 题目稀疏矩阵 ----
const slotsByTeam = computed(() => {
  const map = new Map<string, Map<string, LeaderboardCell>>()
  for (const entry of (leaderboard.value?.entries ?? []) as MatrixEntry[]) {
    if (!entry.teamId) continue
    const cells = new Map<string, LeaderboardCell>()
    for (const cell of entry.cells ?? []) {
      if (cell.competitionChallengeId) {
        cells.set(normalizeChallengeKey(cell.competitionChallengeId), cell)
      }
    }
    map.set(entry.teamId, cells)
  }
  return map
})

function slotFor(teamId?: string, challengeId?: string) {
  if (!teamId || !challengeId) return null
  return slotsByTeam.value.get(teamId)?.get(normalizeChallengeKey(challengeId)) ?? null
}

function cellText(slot: LeaderboardCell, title: string): string {
  const parts = [title]
  if (slot.score !== undefined) parts.push(translate('{score} 分', { score: slot.score }))
  if (slot.bloodRank) parts.push(bloodRankLabel(slot.bloodRank))
  if (slot.solvedAt) parts.push(formatDateTime(slot.solvedAt))
  if (slot.solverName) parts.push(slot.solverName)
  return parts.filter(Boolean).join(' · ')
}

// ---- 分数趋势/详情均由矩阵单元在浏览器派生 ----
function toSeries(entry: MatrixEntry): TrendSeries {
  const solves = (entry.cells ?? [])
    .filter(cell => cell.solvedAt)
    .sort((a, b) => String(a.solvedAt).localeCompare(String(b.solvedAt)))
    .map((cell) => ({
      competitionChallengeId: cell.competitionChallengeId,
      at: cell.solvedAt ?? undefined,
      points: cell.score ?? 0,
      solveOrdinal: cell.bloodRank ? ['First', 'Second', 'Third'].indexOf(String(cell.bloodRank)) + 1 : undefined,
      submitterName: cell.solverName,
    }))
  let score = 0
  const points = solves.map((solve) => ({ at: solve.at, score: score += solve.points ?? 0 }))
  if (score !== (entry.score ?? 0)) {
    points.push({ at: leaderboard.value?.generatedAt, score: entry.score ?? 0 })
  }
  return { teamId: entry.teamId, teamName: entry.teamName, points, solves }
}

const topSeries = computed<TrendSeries[]>(() => entries.value.slice(0, 10).map(toSeries))
const hasAnySeries = computed(() => entries.value.some(entry =>
  (entry.cells?.length ?? 0) > 0 || (entry.score ?? 0) !== 0,
))

// ---- 完整快照上的渐进渲染 ----
const entryBatchSize = 50
const entries = computed<MatrixEntry[]>(() => (leaderboard.value?.entries ?? []) as MatrixEntry[])
const visibleEntryCount = ref(entryBatchSize)
const visibleEntries = computed(() =>
  entries.value.slice(0, visibleEntryCount.value),
)
const hasMoreEntries = computed(() => visibleEntryCount.value < entries.value.length)

watch(entries, () => { visibleEntryCount.value = entryBatchSize })

function showMoreEntries() {
  visibleEntryCount.value = Math.min(
    visibleEntryCount.value + entryBatchSize,
    entries.value.length,
  )
}

// ---- 队伍详情弹窗 ----
const detailOpen = ref(false)
const detailEntry = ref<(typeof entries.value)[number] | null>(null)

const detailSeries = computed<TrendSeries | null>(() => {
  return detailEntry.value ? toSeries(detailEntry.value) : null
})

function openDetail(entry: (typeof entries.value)[number]) {
  detailEntry.value = entry
  detailOpen.value = true
}

// ---- 下载为 Excel(CSV,Excel 可直接打开) ----
function exportCsv() {
  const board = leaderboard.value
  if (!board) return
  const header = [translate("名次"), translate("队伍"), translate("总分"), translate("解题数"), translate("最后得分"), ...challenges.value.map((c) => c.title ?? '题目')]
  const rows = entries.value.map((entry) => [
    entry.rank ?? '',
    entry.teamName ?? '',
    entry.score ?? 0,
    entry.solveCount ?? 0,
    entry.lastScoreAt ? formatDateTime(entry.lastScoreAt) : '',
    ...challenges.value.map((challenge) => {
      const slot = slotFor(entry.teamId, challenge.competitionChallengeId)
      if (!slot) return ''
      return slot.score ?? 0
    }),
  ])
  const escape = (value: unknown) => `"${String(value).replaceAll('"', '""')}"`
  const csv = '﻿' + [header, ...rows].map((row) => row.map(escape).join(',')).join('\r\n')
  const blob = new Blob([csv], { type: 'text/csv;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = `${ctx.competition.value?.title ?? 'leaderboard'}-${translate('记分板')}.csv`
  a.click()
  URL.revokeObjectURL(url)
}

// ---- 方向图标/配色走共享映射(utils/directions.ts),Medal 金银铜走 types.ts 共享常量 ----
</script>

<template>
  <div class="flex flex-col gap-6">
    <Alert v-if="error" variant="destructive">
      <AlertDescription>{{ error }}</AlertDescription>
    </Alert>

    <div v-else-if="loading || polling" class="flex flex-col gap-4">
      <Alert>
        <AlertDescription class="flex items-center gap-2">
          <Spinner class="size-3" /> {{ $t('记分板数据投影中,请稍候…') }} </AlertDescription>
      </Alert>
      <Skeleton class="h-64 w-full" />
    </div>

    <template v-else-if="leaderboard">
      <div class="flex flex-wrap items-center justify-between gap-3">
        <h2 class="flex items-center gap-2 text-display text-xl">
          <Trophy class="size-5 text-primary" /> {{ $t('排行榜') }} </h2>
        <div class="flex items-center gap-4">
          <label class="flex items-center gap-2 text-sm text-muted-foreground">
            <Checkbox v-model="showGroupLabels" /> {{ $t('显示分组标签') }} </label>
          <Button variant="outline" :disabled="!entries.length" @click="exportCsv">
            <Download data-icon="inline-start" /> {{ $t('下载为Excel') }} </Button>
        </div>
      </div>

      <Alert v-if="leaderboard.dataScope === 'Frozen'">
        <AlertDescription>
          {{ $t('排行榜已冻结，以下为截至 {time} 的快照。', { time: formatDateTime(leaderboard.dataAsOf) }) }}
        </AlertDescription>
      </Alert>
      <Empty v-if="leaderboard.dataScope === 'Hidden'" class="border py-12">
        <EmptyHeader>
          <EmptyTitle>{{ $t('排行榜暂不公开') }}</EmptyTitle>
          <EmptyDescription>{{ $t('主办方当前隐藏了排行榜数据') }}</EmptyDescription>
        </EmptyHeader>
      </Empty>

      <template v-else>
        <Card v-if="hasAnySeries">
          <CardContent class="pt-6">
            <ScoreTrendChart
              :title="`${ctx.competition.value?.title ?? ''} - TOP10`"
              :series="topSeries"
              :range-start="ctx.competition.value?.startTime"
              :range-end="ctx.competition.value?.endTime"
            />
          </CardContent>
        </Card>

        <Card>
          <CardContent class="flex flex-col gap-3 pt-6">
            <Empty v-if="!entries.length" class="border py-8">
              <EmptyHeader>
                <EmptyTitle>{{ $t('还没有队伍得分') }}</EmptyTitle>
              </EmptyHeader>
            </Empty>

            <template v-else>
              <div class="overflow-x-auto">
                <Table>
                  <TableHeader>
                    <TableRow v-if="showGroupLabels && directionGroups.length">
                      <TableHead colspan="3" />
                      <TableHead
                        v-for="group in directionGroups"
                        :key="group.direction"
                        :colspan="group.challenges.length"
                        class="border-l text-center"
                      >
                        <span class="inline-flex items-center gap-1.5">
                          <component
                            :is="directionIcon(group.direction)"
                            class="size-4"
                            :class="directionTextClass(group.direction)"
                          />
                          {{ group.direction }}
                        </span>
                      </TableHead>
                    </TableRow>
                    <TableRow>
                      <TableHead class="w-14">{{ $t('名次') }}</TableHead>
                      <TableHead class="sticky left-0 z-10 min-w-44 border-r bg-card">{{ $t('参赛队伍') }}</TableHead>
                      <TableHead class="w-24 text-right">{{ $t('总分') }}</TableHead>
                      <TableHead
                        v-for="challenge in challenges"
                        :key="challenge.competitionChallengeId"
                        class="max-w-16 border-l text-center"
                        :title="challenge.title"
                      >
                        <span class="block truncate px-1">{{ challenge.title }}</span>
                      </TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    <TableRow
                      v-for="entry in visibleEntries"
                      :key="entry.teamId"
                      :class="cn('cursor-pointer focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-inset', (entry.rank ?? 99) <= 3 && 'bg-primary/5 hover:bg-primary/10')"
                      role="button"
                      tabindex="0"
                      :aria-label="$t('查看队伍 {team} 详情', { team: entry.teamName ?? '' })"
                      @click="openDetail(entry)"
                      @keydown.enter="openDetail(entry)"
                      @keydown.space.prevent="openDetail(entry)"
                    >
                      <TableCell>
                        <Medal v-if="(entry.rank ?? 99) <= 3" :class="medalRankClass[entry.rank ?? 0]" class="size-5" />
                        <span v-else class="pl-1 font-mono text-sm tabular-nums">{{ entry.rank }}</span>
                      </TableCell>
                      <TableCell class="sticky left-0 z-10 border-r bg-card">
                        <span class="flex items-center gap-2">
                          <Avatar class="size-8">
                            <AvatarFallback>{{ entry.teamName?.slice(0, 2) ?? '?' }}</AvatarFallback>
                          </Avatar>
                          <span class="font-medium">{{ entry.teamName }}</span>
                        </span>
                      </TableCell>
                      <TableCell class="text-right font-mono font-semibold tabular-nums">{{ entry.score }} pts</TableCell>
                      <TableCell
                        v-for="challenge in challenges"
                        :key="challenge.competitionChallengeId"
                        class="border-l text-center"
                      >
                        <template v-if="slotFor(entry.teamId, challenge.competitionChallengeId)">
                          <Medal
                            class="mx-auto size-5"
                            :class="medalBloodRankClass(slotFor(entry.teamId, challenge.competitionChallengeId)?.bloodRank) ?? 'text-muted-foreground/50'"
                            :title="cellText(slotFor(entry.teamId, challenge.competitionChallengeId)!, challenge.title ?? '')"
                          />
                        </template>
                      </TableCell>
                    </TableRow>
                  </TableBody>
                </Table>
              </div>

              <div class="flex flex-wrap items-center justify-between gap-3 text-sm text-muted-foreground">
                <span>
                  {{ $t('已显示 {visible} / {count} 支队伍', { visible: visibleEntries.length, count: entries.length }) }}
                </span>
                <Button v-if="hasMoreEntries" variant="outline" @click="showMoreEntries">
                  {{ $t('加载更多') }}
                </Button>
              </div>
            </template>
          </CardContent>
        </Card>
      </template>

      <TeamDetailDialog
        v-model:open="detailOpen"
        :competition-id="competitionId"
        :entry="detailEntry"
        :series="detailSeries"
        :challenges="challenges"
        :range-start="ctx.competition.value?.startTime"
        :range-end="ctx.competition.value?.endTime"
      />
    </template>
  </div>
</template>
