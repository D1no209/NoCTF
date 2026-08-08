<script setup lang="ts">
import { getCompetitionEndpoint } from '~/api'
import type { NoCtfapiEndpointsCompetitionsCompetitionResponse } from '~/api'

const route = useRoute()
const competitionId = computed(() => route.params.id as string)

const competition = ref<NoCtfapiEndpointsCompetitionsCompetitionResponse | null>(null)
const loading = ref(true)
const error = ref<string | null>(null)

async function refresh() {
  const { data, error: err } = await getCompetitionEndpoint({
    path: { competitionId: competitionId.value },
  })
  loading.value = false
  if (err || !data) {
    error.value = parseApiError(err, '加载竞赛失败').message
    return
  }
  error.value = null
  competition.value = data
}

await refresh()

// 竞赛生命周期实时变更 → 重新拉取详情
let unwatch: (() => void) | undefined
onMounted(() => {
  unwatch = watchCompetition(competitionId.value, {
    competitionLifecycleChanged: () => void refresh(),
  })
})
onUnmounted(() => unwatch?.())

provide(competitionContextKey, { competition, loading, error, refresh })

const tabs = computed(() => {
  const base = `/competitions/${competitionId.value}`
  return [
    { to: base, label: '概览', exact: true },
    { to: `${base}/challenges`, label: '题目' },
    { to: `${base}/leaderboard`, label: '记分板' },
    { to: `${base}/teams`, label: '队伍' },
    { to: `${base}/events`, label: '动态' },
    { to: `${base}/questions`, label: '咨询' },
    { to: `${base}/my/team`, label: '我的队伍' },
    { to: `${base}/my/submissions`, label: '我的提交' },
  ]
})

function isActive(to: string, exact: boolean) {
  return exact ? route.path === to : route.path.startsWith(to)
}
</script>

<template>
  <div class="mx-auto flex max-w-6xl flex-col gap-6 px-4 py-8">
    <Alert v-if="error" variant="destructive">
      <AlertDescription>{{ error }}</AlertDescription>
    </Alert>

    <template v-else-if="competition">
      <div class="flex flex-col gap-3">
        <div class="flex flex-wrap items-center gap-3">
          <h1 class="text-2xl font-semibold">{{ competition.title }}</h1>
          <ModeBadge :mode="competition.mode" />
          <LifecycleBadge :status="competition.status" />
          <CompetitionCountdown
            :start-time="competition.startTime"
            :end-time="competition.endTime"
            :status="competition.status"
          />
        </div>
        <p class="text-sm text-muted-foreground">
          {{ formatDateTime(competition.startTime) }} ~ {{ formatDateTime(competition.endTime) }}
        </p>
      </div>

      <nav class="flex flex-wrap items-center gap-1 border-b pb-px">
        <Button
          v-for="tab in tabs"
          :key="tab.to"
          variant="ghost"
          size="sm"
          as-child
          :class="isActive(tab.to, tab.exact ?? false) ? 'border-b-2 border-primary rounded-none font-medium' : 'text-muted-foreground'"
        >
          <NuxtLink :to="tab.to">{{ tab.label }}</NuxtLink>
        </Button>
      </nav>

      <NuxtPage />
    </template>

    <div v-else class="flex flex-col gap-4">
      <Skeleton class="h-10 w-2/3" />
      <Skeleton class="h-6 w-1/3" />
      <Skeleton class="h-64 w-full" />
    </div>
  </div>
</template>
