<script setup lang="ts">
import { getLeaderboardEndpoint } from '~/api'
import type {
  NoCtfApplicationScoringLeaderboardLeaderboardBloodSummary,
  NoCtfApplicationScoringLeaderboardLeaderboardResponse,
} from '~/api'

const route = useRoute()
const competitionId = route.params.id as string

const leaderboard = ref<NoCtfApplicationScoringLeaderboardLeaderboardResponse | null>(null)
const loading = ref(true)
const error = ref<string | null>(null)

/** 拉取一次;返回 true 表示拿到 200 数据(不再需要重试)。 */
async function fetchOnce(): Promise<boolean> {
  const { data, error: err, response } = await getLeaderboardEndpoint({ path: { competitionId } })
  loading.value = false
  if (err) {
    error.value = parseApiError(err, '加载记分板失败').message
    return true
  }
  if (response?.status === 202) return false
  error.value = null
  leaderboard.value = data as NoCtfApplicationScoringLeaderboardLeaderboardResponse
  return true
}

const { polling, start: startPolling } = usePolling(fetchOnce, { interval: 2000, timeout: 60_000 })

async function refresh() {
  const done = await fetchOnce()
  if (!done) startPolling()
}

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

const bloodRankLabel: Record<number, string> = { 1: '一血', 2: '二血', 3: '三血' }

const bloodsByRank = computed(() => {
  const groups = new Map<number, NoCtfApplicationScoringLeaderboardLeaderboardBloodSummary[]>()
  for (const blood of leaderboard.value?.bloods ?? []) {
    const rank = blood.bloodRank ?? 0
    const list = groups.get(rank) ?? []
    list.push(blood)
    groups.set(rank, list)
  }
  return [...groups.entries()].sort(([a], [b]) => a - b)
})
</script>

<template>
  <div class="flex flex-col gap-6">
    <Alert v-if="error" variant="destructive">
      <AlertDescription>{{ error }}</AlertDescription>
    </Alert>

    <div v-else-if="loading || polling" class="flex flex-col gap-4">
      <Alert>
        <AlertDescription class="flex items-center gap-2">
          <Spinner class="size-3" />
          记分板数据投影中,请稍候…
        </AlertDescription>
      </Alert>
      <Skeleton class="h-64 w-full" />
    </div>

    <template v-else-if="leaderboard">
      <Alert v-if="leaderboard.dataScope === LeaderboardDataScope.Frozen">
        <AlertDescription>
          排行榜已冻结,以下为截至 {{ formatDateTime(leaderboard.dataAsOf) }} 的快照。
        </AlertDescription>
      </Alert>
      <Alert v-if="leaderboard.stale" variant="destructive">
        <AlertDescription>数据可能过期:排行榜投影暂时落后,稍后会自动刷新。</AlertDescription>
      </Alert>

      <Empty v-if="leaderboard.dataScope === LeaderboardDataScope.Hidden" class="border py-12">
        <EmptyHeader>
          <EmptyTitle>排行榜暂不公开</EmptyTitle>
          <EmptyDescription>主办方当前隐藏了排行榜数据</EmptyDescription>
        </EmptyHeader>
      </Empty>

      <template v-else>
        <Card>
          <CardHeader>
            <CardTitle class="text-base">总分榜</CardTitle>
            <CardDescription>数据时间:{{ formatDateTime(leaderboard.generatedAt) }}</CardDescription>
          </CardHeader>
          <CardContent>
            <Empty v-if="!leaderboard.entries?.length" class="border py-8">
              <EmptyHeader>
                <EmptyTitle>还没有队伍得分</EmptyTitle>
              </EmptyHeader>
            </Empty>
            <Table v-else>
              <TableHeader>
                <TableRow>
                  <TableHead class="w-16">名次</TableHead>
                  <TableHead>队伍</TableHead>
                  <TableHead class="text-right">总分</TableHead>
                  <TableHead class="text-right">解题数</TableHead>
                  <TableHead>最后得分</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                <TableRow v-for="entry in leaderboard.entries" :key="entry.teamId">
                  <TableCell>
                    <Badge :variant="(entry.rank ?? 99) <= 3 ? 'default' : 'secondary'">
                      {{ entry.rank }}
                    </Badge>
                  </TableCell>
                  <TableCell>
                    <NuxtLink
                      :to="`/competitions/${competitionId}/teams/${entry.teamId}`"
                      class="font-medium hover:underline"
                    >
                      {{ entry.teamName }}
                    </NuxtLink>
                  </TableCell>
                  <TableCell class="text-right font-semibold">{{ entry.score }}</TableCell>
                  <TableCell class="text-right">{{ entry.solveCount }}</TableCell>
                  <TableCell class="text-muted-foreground">{{ formatDateTime(entry.lastScoreAt) }}</TableCell>
                </TableRow>
              </TableBody>
            </Table>
          </CardContent>
        </Card>

        <Card v-if="leaderboard.subjects?.length">
          <CardHeader>
            <CardTitle class="text-base">题目通过情况</CardTitle>
          </CardHeader>
          <CardContent>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>题目</TableHead>
                  <TableHead class="text-right">分值</TableHead>
                  <TableHead class="text-right">通过队数</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                <TableRow v-for="subject in leaderboard.subjects" :key="subject.subjectId">
                  <TableCell>
                    <NuxtLink
                      :to="`/competitions/${competitionId}/challenges/${subject.subjectId}`"
                      class="font-medium hover:underline"
                    >
                      {{ subject.subjectName }}
                    </NuxtLink>
                  </TableCell>
                  <TableCell class="text-right">{{ subject.score }}</TableCell>
                  <TableCell class="text-right">{{ subject.successCount }}</TableCell>
                </TableRow>
              </TableBody>
            </Table>
          </CardContent>
        </Card>

        <Card v-if="bloodsByRank.length">
          <CardHeader>
            <CardTitle class="text-base">血榜</CardTitle>
          </CardHeader>
          <CardContent class="flex flex-col gap-4">
            <div v-for="[rank, bloods] in bloodsByRank" :key="rank" class="flex flex-col gap-2">
              <h3 class="text-sm font-semibold">{{ bloodRankLabel[rank] ?? `${rank} 血` }}</h3>
              <ul class="flex flex-col gap-1 text-sm">
                <li v-for="blood in bloods" :key="`${blood.slotKey}-${blood.teamId}`" class="flex items-center gap-2">
                  <Badge variant="outline">{{ blood.slotKey }}</Badge>
                  <span class="font-medium">{{ blood.teamName }}</span>
                  <span class="text-muted-foreground">{{ formatDateTime(blood.occurredAt) }}</span>
                </li>
              </ul>
            </div>
          </CardContent>
        </Card>
      </template>
    </template>
  </div>
</template>
