<script setup lang="ts">
import { Award, Medal, Target, Users } from '@lucide/vue'
import { getTeamEndpoint } from '~/api'
import type { NoCtfapiEndpointsTeamsTeamResponse } from '~/api'
import type { echarts } from '~/utils/echarts'
import { normalizeChallengeKey } from './types'
import type { ChallengeInfo, TrendSeries } from './types'

const props = defineProps<{
  competitionId: string
  entry: { teamId?: string; teamName?: string; rank?: number; score?: number; solveCount?: number } | null
  series?: TrendSeries | null
  challenges: ChallengeInfo[]
  rangeStart?: string | null
  rangeEnd?: string | null
}>()

const open = defineModel<boolean>('open', { default: false })

// 队伍公开资料(头像/成员数)
const team = ref<NoCtfapiEndpointsTeamsTeamResponse | null>(null)
watch([open, () => props.entry?.teamId], async ([isOpen, teamId]) => {
  if (!isOpen || !teamId) return
  team.value = null
  const { data, error } = await getTeamEndpoint({ path: { competitionId: props.competitionId, teamId } })
  if (!error && data) team.value = data
})

const challengeTitle = computed(() => {
  const map = new Map<string, string>()
  for (const challenge of props.challenges) {
    if (challenge.competitionChallengeId) {
      map.set(normalizeChallengeKey(challenge.competitionChallengeId), challenge.title ?? translate('题目'))
    }
  }
  return (id?: string) => map.get(normalizeChallengeKey(id)) ?? translate('题目')
})

const solves = computed(() =>
  [...(props.series?.solves ?? [])].sort((a, b) => String(b.at).localeCompare(String(a.at))),
)
// 解题分布雷达:各方向解题数
const radarOption = computed<echarts.EChartsCoreOption>(() => {
  const counts = new Map<string, number>()
  for (const challenge of props.challenges) {
    const direction = challenge.direction || translate('未分类')
    if (!counts.has(direction)) counts.set(direction, 0)
  }
  for (const solve of props.series?.solves ?? []) {
    const challenge = props.challenges.find(
      (c) => normalizeChallengeKey(c.competitionChallengeId) === normalizeChallengeKey(solve.competitionChallengeId),
    )
    const direction = challenge?.direction || translate('未分类')
    counts.set(direction, (counts.get(direction) ?? 0) + 1)
  }
  const indicators = [...counts.entries()].map(([name, value]) => ({
    name,
    max: Math.max(2, ...counts.values()),
  }))
  return {
    radar: { indicator: indicators, radius: '62%', splitNumber: 3 },
    series: [
      {
        type: 'radar',
        data: [{ name: translate("解题数量"), value: indicators.map((i) => counts.get(i.name) ?? 0) }],
        areaStyle: { opacity: 0.35 },
      },
    ],
  }
})

// 题目分数占比饼图
const challengePieOption = computed<echarts.EChartsCoreOption>(() => {
  const totals = new Map<string, number>()
  for (const solve of props.series?.solves ?? []) {
    const title = challengeTitle.value(solve.competitionChallengeId)
    totals.set(title, (totals.get(title) ?? 0) + (solve.points ?? 0))
  }
  return {
    tooltip: { trigger: 'item', valueFormatter: (v: number | string) => `${v} pts` },
    legend: { type: 'scroll', orient: 'vertical', right: 0, top: 'middle' },
    series: [
      {
        type: 'pie',
        radius: ['30%', '65%'],
        center: ['38%', '50%'],
        label: { show: false },
        data: [...totals.entries()].map(([name, value]) => ({ name, value })),
      },
    ],
  }
})

// 成员贡献饼图(按提交人得分)
const memberPieOption = computed<echarts.EChartsCoreOption>(() => {
  const totals = new Map<string, number>()
  for (const solve of props.series?.solves ?? []) {
    const name = solve.submitterName || translate('未知成员')
    totals.set(name, (totals.get(name) ?? 0) + (solve.points ?? 0))
  }
  return {
    tooltip: { trigger: 'item', valueFormatter: (v: number | string) => `${v} pts` },
    series: [
      {
        type: 'pie',
        radius: '65%',
        data: [...totals.entries()].map(([name, value]) => ({ name, value })),
      },
    ],
  }
})

const hasCharts = computed(() => (props.series?.solves?.length ?? 0) > 0)
</script>

<template>
  <Dialog v-model:open="open">
    <DialogContent class="max-h-[90vh] overflow-y-auto sm:max-w-4xl">
      <DialogHeader>
        <DialogTitle class="sr-only">{{ $t('队伍详情') }}</DialogTitle>
      </DialogHeader>

      <div v-if="entry" class="flex flex-col gap-6">
        <div class="flex items-center gap-4">
          <Avatar class="size-14">
            <AvatarImage v-if="team?.avatarUrl" :src="team.avatarUrl" :alt="entry.teamName ?? ''" />
            <AvatarFallback class="text-lg">{{ entry.teamName?.slice(0, 2) ?? '?' }}</AvatarFallback>
          </Avatar>
          <div>
            <h2 class="text-2xl font-bold">{{ entry.teamName }}</h2>
            <p class="text-sm text-muted-foreground">{{ $t('报名时间：{time}', { time: formatDateTime(team?.registeredAt) }) }}</p>
          </div>
        </div>

        <div class="grid grid-cols-2 gap-3 lg:grid-cols-4">
          <Card>
            <CardContent class="flex items-center gap-3 pt-6">
              <Medal class="size-8 text-amber-500" />
              <div>
                <p class="text-xs text-muted-foreground">{{ $t('排名') }}</p>
                <p class="text-xl font-bold">#{{ entry.rank }}</p>
              </div>
            </CardContent>
          </Card>
          <Card>
            <CardContent class="flex items-center gap-3 pt-6">
              <Target class="size-8 text-emerald-500" />
              <div>
                <p class="text-xs text-muted-foreground">{{ $t('解题数') }}</p>
                <p class="text-xl font-bold">{{ entry.solveCount ?? 0 }}</p>
              </div>
            </CardContent>
          </Card>
          <Card>
            <CardContent class="flex items-center gap-3 pt-6">
              <Award class="size-8 text-sky-500" />
              <div>
                <p class="text-xs text-muted-foreground">{{ $t('总分') }}</p>
                <p class="text-xl font-bold">{{ entry.score ?? 0 }} pts</p>
              </div>
            </CardContent>
          </Card>
          <Card>
            <CardContent class="flex items-center gap-3 pt-6">
              <Users class="size-8 text-violet-500" />
              <div>
                <p class="text-xs text-muted-foreground">{{ $t('成员数') }}</p>
                <p class="text-xl font-bold">{{ team?.memberIds?.length ?? '—' }}</p>
              </div>
            </CardContent>
          </Card>
        </div>

        <div class="grid gap-4 lg:grid-cols-2">
          <Card>
            <CardHeader>
              <CardTitle class="flex items-center gap-2 text-base">
                <Target class="size-4" /> {{ $t('解题分布') }} </CardTitle>
            </CardHeader>
            <CardContent>
              <MiniChart v-if="challenges.length" :option="radarOption" />
              <p v-else class="text-sm text-muted-foreground">{{ $t('暂无数据') }}</p>
            </CardContent>
          </Card>
          <Card>
            <CardHeader>
              <CardTitle class="flex items-center gap-2 text-base">
                <Award class="size-4" /> {{ $t('积分变化趋势') }} </CardTitle>
            </CardHeader>
            <CardContent>
              <ScoreTrendChart
                :series="series ? [series] : []"
                :range-start="rangeStart"
                :range-end="rangeEnd"
                height="260px"
              />
            </CardContent>
          </Card>
          <Card>
            <CardHeader>
              <CardTitle class="flex items-center gap-2 text-base">
                <Award class="size-4" /> {{ $t('题目分数占比') }} </CardTitle>
            </CardHeader>
            <CardContent>
              <MiniChart v-if="hasCharts" :option="challengePieOption" />
              <p v-else class="text-sm text-muted-foreground">{{ $t('还没有得分记录') }}</p>
            </CardContent>
          </Card>
          <Card>
            <CardHeader>
              <CardTitle class="flex items-center gap-2 text-base">
                <Users class="size-4" /> {{ $t('成员贡献') }} </CardTitle>
            </CardHeader>
            <CardContent>
              <MiniChart v-if="hasCharts" :option="memberPieOption" />
              <p v-else class="text-sm text-muted-foreground">{{ $t('还没有得分记录') }}</p>
            </CardContent>
          </Card>
        </div>

        <Card>
          <CardHeader>
            <CardTitle class="text-base">{{ $t('详细记录') }}</CardTitle>
          </CardHeader>
          <CardContent>
            <Tabs default-value="solves">
              <TabsList>
                <TabsTrigger value="solves">
                  <Target class="size-4" />
                  {{ $t('解题记录（{count}）', { count: solves.length }) }}
                </TabsTrigger>
              </TabsList>
              <TabsContent value="solves">
                <ScrollArea class="h-72">
                  <ul class="flex flex-col gap-2 pr-4">
                    <li
                      v-for="(solve, index) in solves"
                      :key="index"
                      class="flex items-center justify-between gap-3 rounded-md border border-l-4 border-l-emerald-500 px-3 py-2"
                    >
                      <div class="flex flex-col gap-1">
                        <span class="flex items-center gap-2 font-medium">
                          <Badge variant="secondary">#{{ solve.solveOrdinal ?? '—' }}</Badge>
                          {{ challengeTitle(solve.competitionChallengeId) }}
                        </span>
                        <span class="flex items-center gap-3 text-xs text-muted-foreground">
                          <span class="flex items-center gap-1">
                            <Users class="size-3" />
                            {{ solve.submitterName || $t('未知成员') }}
                          </span>
                          <span>{{ formatDateTime(solve.at) }}</span>
                        </span>
                      </div>
                      <Badge class="bg-emerald-600 text-white hover:bg-emerald-600">+{{ solve.points ?? 0 }} pts</Badge>
                    </li>
                    <li v-if="!solves.length" class="py-8 text-center text-sm text-muted-foreground">{{ $t('还没有解题记录') }}</li>
                  </ul>
                </ScrollArea>
              </TabsContent>
            </Tabs>
          </CardContent>
        </Card>
      </div>
    </DialogContent>
  </Dialog>
</template>
