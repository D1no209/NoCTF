<script setup lang="ts">
import { FileCheck, LayoutDashboard, MessageCircleQuestion, Puzzle, Trophy, UserRound } from '@lucide/vue'
import { getCompetitionEndpoint, getMyTeamEndpoint } from '~/api'
import type { NoCtfapiEndpointsCompetitionsCompetitionResponse, NoCtfapiEndpointsTeamsTeamResponse } from '~/api'
import { competitionWorkspaceNavigationKey } from '~/components/app/workspace-nav'
import type { WorkspaceNavGroup } from '~/components/app/workspace-nav'

const route = useRoute()
const competitionId = computed(() => route.params.id as string)
const isControlScreen = computed(() => [
  `/competitions/${competitionId.value}/live`,
  `/competitions/${competitionId.value}/awdp-live`,
].includes(route.path))
const usesParticipantWorkspace = computed(() => {
  const base = `/competitions/${competitionId.value}`
  return [
    `${base}/challenges`,
    `${base}/leaderboard`,
    `${base}/questions`,
    `${base}/my/team`,
    `${base}/my/submissions`,
  ].includes(route.path)
})

const competition = ref<NoCtfapiEndpointsCompetitionsCompetitionResponse | null>(null)
const myTeam = ref<NoCtfapiEndpointsTeamsTeamResponse | null>(null)
const loading = ref(true)
const error = ref<string | null>(null)
const { user } = useAuth()
const hasCompetitionStaffAccess = computed(() => competition.value?.administrationRole != null)
const hasParticipantChallengeAccess = computed(() =>
  myTeam.value?.registrationStatus === 'Approved' && !myTeam.value.isBanned,
)

async function refreshMyTeam() {
  if (!user.value || hasCompetitionStaffAccess.value) {
    myTeam.value = null
    return
  }
  const { data, error: teamError } = await getMyTeamEndpoint({
    path: { competitionId: competitionId.value },
  })
  myTeam.value = teamError || !data ? null : data
}

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
await refreshMyTeam()

watch(
  [() => user.value?.userId, hasCompetitionStaffAccess],
  () => void refreshMyTeam(),
)

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
  const canReadChallenges = hasCompetitionStaffAccess.value || hasParticipantChallengeAccess.value
  return [
    {
      label: translate("竞赛"),
      items: [
        { to: base, label: translate("概览"), icon: LayoutDashboard, exact: true },
        ...(challengesVisible && canReadChallenges ? [{ to: `${base}/challenges`, label: translate("题目"), icon: Puzzle }] : []),
        { to: `${base}/leaderboard`, label: translate("记分板"), icon: Trophy },
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

provide(competitionWorkspaceNavigationKey, navGroups)
</script>

<template>
  <NuxtPage v-if="isControlScreen" />

  <div
    v-else-if="competition && usesParticipantWorkspace"
    class="mx-auto flex w-full max-w-[120rem] flex-col gap-4 px-3 py-4 md:px-5"
  >
    <div class="flex flex-col gap-1 border-b pb-4">
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
