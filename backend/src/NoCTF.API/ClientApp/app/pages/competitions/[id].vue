<script setup lang="ts">
import { Activity, FileCheck, LayoutDashboard, MessageCircleQuestion, Puzzle, Trophy, UserRound, Users } from '@lucide/vue'
import { getCompetitionEndpoint } from '~/api'
import type { NoCtfapiEndpointsCompetitionsCompetitionResponse } from '~/api'
import type { WorkspaceNavGroup } from '~/components/app/workspace-nav'

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
    error.value = parseApiError(err, translate("加载竞赛失败")).message
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

const navGroups = computed<WorkspaceNavGroup[]>(() => {
  const base = `/competitions/${competitionId.value}`
  const challengesVisible = competition.value?.status === CompetitionStatus.Running
    || competition.value?.status === CompetitionStatus.Paused
    || competition.value?.status === CompetitionStatus.Finished
  return [
    {
      label: translate("竞赛"),
      items: [
        { to: base, label: translate("概览"), icon: LayoutDashboard, exact: true },
        ...(challengesVisible ? [{ to: `${base}/challenges`, label: translate("题目"), icon: Puzzle }] : []),
        { to: `${base}/leaderboard`, label: translate("记分板"), icon: Trophy },
        { to: `${base}/teams`, label: translate("队伍"), icon: Users },
        { to: `${base}/events`, label: translate("动态"), icon: Activity },
      ],
    },
    {
      label: translate("互动"),
      items: [
        { to: `${base}/questions`, label: translate("咨询"), icon: MessageCircleQuestion },
      ],
    },
    {
      label: translate("我的"),
      items: [
        { to: `${base}/my/team`, label: translate("我的队伍"), icon: UserRound },
        { to: `${base}/my/submissions`, label: translate("我的提交"), icon: FileCheck },
      ],
    },
  ]
})
</script>

<template>
  <AppWorkspaceNav v-if="competition" :groups="navGroups" :title="competition.title">
    <div class="mx-auto flex w-full max-w-5xl flex-col gap-6 px-4 py-8 md:px-6">
      <div class="flex flex-col gap-3">
        <div class="flex flex-wrap items-center gap-3">
          <h1 class="text-2xl font-bold tracking-tight md:text-3xl">{{ competition.title }}</h1>
          <ModeBadge :mode="competition.mode" />
          <LifecycleBadge :status="competition.status" />
          <CompetitionCountdown
            :start-time="competition.startTime"
            :end-time="competition.endTime"
            :status="competition.status"
            class="font-mono font-medium text-primary"
          />
        </div>
        <p class="font-mono text-xs text-muted-foreground tabular-nums">
          {{ formatDateTime(competition.startTime) }} ~ {{ formatDateTime(competition.endTime) }}
        </p>
      </div>

      <NuxtPage />
    </div>
  </AppWorkspaceNav>

  <div v-else class="mx-auto flex max-w-6xl flex-col gap-6 px-4 py-8">
    <Alert v-if="error" variant="destructive">
      <AlertDescription>{{ error }}</AlertDescription>
    </Alert>

    <div v-else class="flex flex-col gap-4">
      <Skeleton class="h-10 w-2/3" />
      <Skeleton class="h-6 w-1/3" />
      <Skeleton class="h-64 w-full" />
    </div>
  </div>
</template>
