import { markRaw } from 'vue'

import { ClipboardCheck, FileText, GitBranch, LayoutDashboard, MessageCircleQuestion, Puzzle, Trophy, UserRound } from '@lucide/vue'
import { getCompetitionEndpoint, getLeaderboardEndpoint, getMyTeamEndpoint, getPlayerCompetitionProgression } from '../../../api'
import type { NoCtfapiEndpointsCompetitionsCompetitionResponse, NoCtfapiEndpointsCompetitionsScoreboardTeamResponse, NoCtfapiEndpointsTeamsTeamResponse } from '../../../api'
import { competitionWorkspaceNavigationKey } from '../../app/workspace-nav'
import type { WorkspaceNavGroup } from '../../app/workspace-nav'
import { createTrailingRefresh } from '../../../lib/latest-page-refresh'
import CompetitionCountdownComponent from '../../competitions/CompetitionCountdown.vue'
import LifecycleBadgeComponent from '../../competitions/LifecycleBadge.vue'
import { useCompetitionAnnouncementCatchUp } from '../../competition/useCompetitionAnnouncementCatchUp'
import { competitionPath, isCompetitionOverviewPath } from '../../../utils/app-routes'

/** Owns state, effects and commands for CompetitionsByIdPage. */
export function useCompetitionsByIdPage() {
  const route = useRoute()
  const router = useRouter()

  const competitionId = computed(() => route.params.id as string)

  const isOverview = computed(() => isCompetitionOverviewPath(route.path))

  const isControlScreen = computed(() => [
    `/competitions/${competitionId.value}/live`,
    `/competitions/${competitionId.value}/awdp-live`,
  ].includes(route.path))

  const isWriteUpReview = computed(() =>
    route.path === `/competitions/${competitionId.value}/writeups`,
  )

  const isProgression = computed(() =>
    route.path === `/competitions/${competitionId.value}/progression`,
  )

  const competition = ref<NoCtfapiEndpointsCompetitionsCompetitionResponse | null>(null)

  const myTeam = ref<NoCtfapiEndpointsTeamsTeamResponse | null>(null)

  const myStanding = ref<NoCtfapiEndpointsCompetitionsScoreboardTeamResponse | null>(null)

  const standingLoading = ref(false)

  const teamLoadError = ref<string | null>(null)

  const standingError = ref<string | null>(null)

  const loading = ref(true)

  const error = ref<string | null>(null)

  const { user } = useAuth()

  const { refreshMissedAnnouncements } = useCompetitionAnnouncementCatchUp(competitionId)

  const hasCompetitionStaffAccess = computed(() => competition.value?.administrationRole != null)

  const hasParticipantChallengeAccess = computed(() =>
    myTeam.value?.registrationStatus === 'Approved' && !myTeam.value.isBanned,
  )

  const progressionEnabled = ref(false)

  let progressionRequestId = 0

  async function refreshProgressionEnabled() {
    const requestId = ++progressionRequestId
    if (competition.value?.mode !== 'Ctf'
      || (!hasCompetitionStaffAccess.value && !hasParticipantChallengeAccess.value)) {
      progressionEnabled.value = false
      return
    }
    const { data } = await getPlayerCompetitionProgression({
      path: { competitionId: competitionId.value },
    })
    if (requestId === progressionRequestId)
      progressionEnabled.value = data?.enabled === true
  }

  watch([competition, myTeam, () => user.value?.userId], () => {
    void refreshProgressionEnabled()
  }, { immediate: true })

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
      teamLoadError.value = parseApiError(teamError, translate("ui.failedToLoadYourTeam")).message
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
      standingError.value = parseApiError(requestError, translate("ui.failedToLoadYourTeamRanking")).message
      return
    }
    standingError.value = null
    myStanding.value = data.teams?.find(team => team.teamId === myTeam.value?.id) ?? null
  }

  const refreshStandingLatest = createTrailingRefresh(refreshMyStanding)

  async function refresh(): Promise<'loaded' | 'not-found' | 'failed'> {
    const { data, error: err, response } = await getCompetitionEndpoint({
      path: { competitionId: competitionId.value },
    })
    loading.value = false
    if (err || !data) {
      error.value = parseApiError(err, translate("ui.loadingCompetitionFailed")).message
      progressionEnabled.value = false
      return response?.status === 404 ? 'not-found' : 'failed'
    }
    error.value = null
    competition.value = data
    return 'loaded'
  }

  async function handleAudienceChanged() {
    const result = await refresh()
    if (result === 'not-found') {
      await router.replace('/competitions')
      return
    }
    if (result === 'loaded' && !isWriteUpReview.value)
      await refreshMyTeam()
  }

  watch(
    () => user.value?.userId,
    () => {
      if (isOverview.value) return
      if (!isWriteUpReview.value) void refreshMyTeam()
      void refreshMissedAnnouncements()
    },
  )

  let unwatch: (() => void) | undefined

  onMounted(() => {
    if (isOverview.value) return
    unwatch = watchCompetition(competitionId.value, {
      competitionLifecycleChanged: () => {
        void refresh()
        if (!isWriteUpReview.value) void refreshStandingLatest()
      },
      competitionEventChanged: event => {
        if (event.kind === 'CompetitionAudienceChanged')
          void handleAudienceChanged()
        if (event.kind === 'CompetitionUpdated')
          void refreshProgressionEnabled()
        if (event.kind === 'AnnouncementPublished')
          void refreshMissedAnnouncements()
      },
      scoreboardUpdated: () => {
        if (!isWriteUpReview.value) void refreshStandingLatest()
      },
      onReconnected: () => {
        if (!isWriteUpReview.value) void refreshStandingLatest()
        void refreshProgressionEnabled()
        void refreshMissedAnnouncements()
      },
    })
  })

  onUnmounted(() => {
    progressionRequestId++
    unwatch?.()
  })

  provide(competitionContextKey, {
    competition,
    loading,
    error,
    refresh: async () => { await refresh() },
  })

  const navGroups = computed<WorkspaceNavGroup[]>(() => {
    const base = `/competitions/${competitionId.value}`
    const challengesVisible = competition.value?.status === 'Running'
      || competition.value?.status === 'Paused'
      || competition.value?.status === 'Finished'
    const canReadChallenges = hasCompetitionStaffAccess.value || hasParticipantChallengeAccess.value
    return [
      {
        label: translate("ui.competitions"),
        items: [
          { to: competitionPath(competitionId.value), label: translate("ui.overview"), icon: LayoutDashboard, exact: true },
          ...(challengesVisible && canReadChallenges ? [{ to: `${base}/challenges`, label: translate("ui.challenge"), icon: Puzzle }] : []),
          ...(competition.value?.mode === 'Ctf' && canReadChallenges && progressionEnabled.value
            ? [{ to: `${base}/progression`, label: translate('progression.title'), icon: GitBranch }]
            : []),
          { to: `${base}/leaderboard`, label: translate("ui.leaderboard"), icon: Trophy },
          { to: `${base}/questions`, label: translate("ui.questions"), icon: MessageCircleQuestion },
        ],
      },
      {
        label: translate("ui.mine"),
        items: [
          { to: `${base}/my/team`, label: translate("ui.myTeam"), icon: UserRound },
          ...(hasParticipantChallengeAccess.value
            ? [{ to: `${base}/my/writeup`, label: translate("writeUp.myWriteUp"), icon: FileText }]
            : []),
        ],
      },
      ...(hasCompetitionStaffAccess.value
        ? [{
            label: translate("ui.management"),
            items: [
              { to: `${base}/writeups`, label: translate("writeUp.review"), icon: ClipboardCheck },
            ],
          }]
        : []),
    ]
  })

  provide(competitionWorkspaceNavigationKey, navGroups)

  const CompetitionCountdown = markRaw(CompetitionCountdownComponent)

  const LifecycleBadge = markRaw(LifecycleBadgeComponent)


  async function initialize() {
    if (isOverview.value) return
    await refresh()
    if (!isWriteUpReview.value) await refreshMyTeam()
    await refreshMissedAnnouncements()
  }

  return {
      initialize,
      isOverview,
      isControlScreen,
      isWriteUpReview,
      isProgression,
      competition,
      myStanding,
      standingLoading,
      teamLoadError,
      standingError,
      error,
      refreshMyTeam,
      refreshMyStanding,
      CompetitionCountdown,
      LifecycleBadge,
    }
}

export type CompetitionsByIdPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useCompetitionsByIdPage>>>
