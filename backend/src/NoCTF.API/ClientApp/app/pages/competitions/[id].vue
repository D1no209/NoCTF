<script setup lang="ts">
import { LayoutDashboard, MessageCircleQuestion, Puzzle, Trophy, UserRound } from '@lucide/vue'
import { getCompetitionEndpoint, getLeaderboardEndpoint, getMyTeamEndpoint } from '~/api'
import type {
  NoCtfapiEndpointsCompetitionsCompetitionResponse,
  NoCtfapiEndpointsCompetitionsScoreboardTeamResponse,
  NoCtfapiEndpointsTeamsTeamResponse,
} from '~/api'
import { competitionWorkspaceNavigationKey } from '~/components/app/workspace-nav'
import type { WorkspaceNavGroup } from '~/components/app/workspace-nav'
import { createTrailingRefresh } from '~/lib/latest-page-refresh'

const route = useRoute()
const competitionId = computed(() => route.params.id as string)
const isControlScreen = computed(() => [
  `/competitions/${competitionId.value}/live`,
  `/competitions/${competitionId.value}/awdp-live`,
].includes(route.path))
const competition = ref<NoCtfapiEndpointsCompetitionsCompetitionResponse | null>(null)
const myTeam = ref<NoCtfapiEndpointsTeamsTeamResponse | null>(null)
const myStanding = ref<NoCtfapiEndpointsCompetitionsScoreboardTeamResponse | null>(null)
const standingLoading = ref(false)
const teamLoadError = ref<string | null>(null)
const standingError = ref<string | null>(null)
const loading = ref(true)
const error = ref<string | null>(null)
const { user } = useAuth()
const hasCompetitionStaffAccess = computed(() => competition.value?.administrationRole != null)
const hasParticipantChallengeAccess = computed(() =>
  myTeam.value?.registrationStatus === 'Approved' && !myTeam.value.isBanned,
)

async function refreshMyTeam() {
  if (!user.value) {
    myTeam.value = null
    myStanding.value = null
    teamLoadError.value = null
    standingError.value = null
    return
  }
  const { data, error: teamError, response } = await getMyTeamEndpoint({
    path: { competitionId: competitionId.value },
  })
  if (response?.status === 404) {
    myTeam.value = null
    teamLoadError.value = null
    standingError.value = null
    return
  }
  if (teamError || !data) {
    teamLoadError.value = parseApiError(teamError, translate('加载我的队伍失败')).message
    return
  }
  teamLoadError.value = null
  myTeam.value = data
  await refreshMyStanding()
}

async function refreshMyStanding(): Promise<void> {
  if (myTeam.value?.registrationStatus !== 'Approved' || myTeam.value.isBanned || !myTeam.value.id) {
    myStanding.value = null
    standingLoading.value = false
    standingError.value = null
    return
  }
  standingLoading.value = myStanding.value === null
  const { data, error: requestError, response } = await getLeaderboardEndpoint({
    path: { competitionId: competitionId.value },
    query: { endingRound: null },
  })
  standingLoading.value = false
  if (response?.status === 404) {
    myStanding.value = null
    standingError.value = null
    return
  }
  if (requestError || !data || !('teams' in data)) {
    standingError.value = parseApiError(requestError, translate('加载本队排名失败')).message
    return
  }
  standingError.value = null
  myStanding.value = data.teams?.find(team => team.teamId === myTeam.value?.id) ?? null
}

const refreshStandingLatest = createTrailingRefresh(refreshMyStanding)

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
  () => user.value?.userId,
  () => void refreshMyTeam(),
)

// 竞赛生命周期实时变更 → 重新拉取详情
let unwatch: (() => void) | undefined
onMounted(() => {
  unwatch = watchCompetition(competitionId.value, {
    competitionLifecycleChanged: () => {
      void refresh()
      void refreshStandingLatest()
    },
    scoreboardUpdated: () => void refreshStandingLatest(),
    onReconnected: () => void refreshStandingLatest(),
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
      ],
    },
  ]
})

provide(competitionWorkspaceNavigationKey, navGroups)
</script>

<template>
  <NuxtPage v-if="isControlScreen" />

  <div
    v-else-if="competition"
    class="mx-auto flex w-full max-w-[120rem] flex-col gap-4 px-3 py-4 md:px-5"
  >
    <div class="flex flex-col justify-between gap-3 border-b pb-4 sm:flex-row sm:items-start">
      <div class="flex flex-col gap-1">
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

      <div v-if="standingLoading" class="flex shrink-0 gap-2" :aria-label="$t('本队排名加载中')">
        <Skeleton class="h-12 w-24" />
        <Skeleton class="h-12 w-28" />
      </div>
      <dl v-else-if="myStanding" class="flex shrink-0 divide-x rounded-lg border bg-card/60">
        <div class="min-w-24 px-4 py-2 text-right">
          <dt class="text-xs text-muted-foreground">{{ $t('本队排名') }}</dt>
          <dd class="font-mono text-lg font-semibold tabular-nums">{{ myStanding.rank ? `#${myStanding.rank}` : '-' }}</dd>
        </div>
        <div class="min-w-28 px-4 py-2 text-right">
          <dt class="text-xs text-muted-foreground">{{ $t('本队积分') }}</dt>
          <dd class="font-mono text-lg font-semibold tabular-nums text-primary">{{ myStanding.totalScore ?? 0 }} pts</dd>
        </div>
      </dl>
    </div>
    <Alert v-if="teamLoadError" variant="destructive">
      <AlertDescription class="flex flex-wrap items-center justify-between gap-3">
        <span>{{ teamLoadError }}</span>
        <Button type="button" size="sm" variant="outline" @click="refreshMyTeam">{{ $t('重新加载') }}</Button>
      </AlertDescription>
    </Alert>
    <Alert v-else-if="standingError" variant="destructive">
      <AlertDescription class="flex flex-wrap items-center justify-between gap-3">
        <span>{{ standingError }}</span>
        <Button type="button" size="sm" variant="outline" @click="refreshMyStanding">{{ $t('重新加载') }}</Button>
      </AlertDescription>
    </Alert>
    <NuxtPage />
  </div>

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
