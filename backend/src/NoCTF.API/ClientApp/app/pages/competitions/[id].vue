<script setup lang="ts">
import { Activity, FileCheck, LayoutDashboard, MessageCircleQuestion, MonitorUp, Orbit, Puzzle, Trophy, UserRound, Users } from '@lucide/vue'
import { getCompetitionEndpoint } from '~/api'
import type { NoCtfapiEndpointsCompetitionsCompetitionResponse } from '~/api'
import type { WorkspaceNavGroup } from '~/components/app/workspace-nav'

const route = useRoute()
const competitionId = computed(() => route.params.id as string)
const isControlScreen = computed(() => route.path === `/competitions/${competitionId.value}/screen`
  || route.path === `/competitions/${competitionId.value}/live`)

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
  const challengesVisible = competition.value?.status === 'Running'
    || competition.value?.status === 'Paused'
    || competition.value?.status === 'Finished'
  return [
    {
      label: translate("竞赛"),
      items: [
        { to: base, label: translate("概览"), icon: LayoutDashboard, exact: true },
        ...(challengesVisible ? [{ to: `${base}/challenges`, label: translate("题目"), icon: Puzzle }] : []),
        { to: `${base}/leaderboard`, label: translate("记分板"), icon: Trophy },
        ...(competition.value?.mode === 'Ctf' ? [{ to: `${base}/screen`, label: translate("中控大屏"), icon: MonitorUp }] : []),
        ...(competition.value?.mode === 'Ctf' ? [{ to: `${base}/live`, label: translate("3D 大屏"), icon: Orbit }] : []),
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
  <NuxtPage v-if="isControlScreen" />

  <AppWorkspaceNav v-else-if="competition" :groups="navGroups" :title="competition.title">
    <div class="mx-auto flex w-full max-w-5xl flex-col gap-6 px-4 py-8 md:px-6">
      <div class="flex flex-col gap-2">
        <div class="flex flex-wrap items-center gap-x-3 gap-y-2">
          <h1 class="text-display text-2xl md:text-3xl">{{ competition.title }}</h1>
          <ModeBadge :mode="competition.mode" />
          <LifecycleBadge :status="competition.status" />
        </div>
        <div class="flex flex-wrap items-center gap-x-3 gap-y-1 font-mono text-xs tabular-nums md:text-sm">
          <span class="text-muted-foreground">
            {{ formatDateTime(competition.startTime) }} ~ {{ formatDateTime(competition.endTime) }}
          </span>
          <CompetitionCountdown
            :start-time="competition.startTime"
            :end-time="competition.endTime"
            :status="competition.status"
            class="font-medium text-primary"
          />
        </div>
      </div>

      <NuxtPage />
    </div>
  </AppWorkspaceNav>

  <div v-else class="mx-auto flex w-full max-w-5xl flex-col gap-6 px-4 py-8">
    <Alert v-if="error" variant="destructive">
      <AlertDescription>{{ error }}</AlertDescription>
    </Alert>

    <div v-else class="flex flex-col gap-6">
      <div class="flex flex-col gap-2">
        <div class="flex items-center gap-3">
          <Skeleton class="h-9 w-64" />
          <Skeleton class="h-6 w-14" />
          <Skeleton class="h-6 w-14" />
        </div>
        <Skeleton class="h-4 w-80" />
      </div>
      <Skeleton class="h-64 w-full" />
    </div>
  </div>
</template>
