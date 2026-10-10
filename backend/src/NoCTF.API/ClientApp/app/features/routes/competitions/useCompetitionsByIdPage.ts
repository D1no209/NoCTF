import CompetitionTeamBanScreenComponent from '../../competition/CompetitionTeamBanScreen.vue'
import { message as describeMessage } from '../../../utils/i18n'
import type { UiMessage } from '../../../utils/i18n'
import { markRaw } from 'vue'

import { ArrowLeft, ClipboardCheck, FileText, GitBranch, LayoutDashboard, MessageCircleQuestion, Puzzle, Trophy, UserRound } from '@lucide/vue'
import { getCompetitionEndpoint, getLeaderboardEndpoint, getMyTeamEndpoint, getPlayerCompetitionProgression } from '../../../api'
import type { NoCtfapiEndpointsCompetitionsCompetitionResponse, NoCtfapiEndpointsCompetitionsScoreboardTeamResponse, NoCtfapiEndpointsTeamsTeamResponse } from '../../../api'
import { competitionWorkspaceNavigationKey } from '../../app/workspace-nav'
import type { WorkspaceNavGroup } from '../../app/workspace-nav'
import { createTrailingRefresh } from '../../../lib/latest-page-refresh'
import CompetitionCountdownComponent from '../../competitions/CompetitionCountdown.vue'
import LifecycleBadgeComponent from '../../competitions/LifecycleBadge.vue'
import { useCompetitionAnnouncementCatchUp } from '../../competition/useCompetitionAnnouncementCatchUp'
import { adminCompetitionPath, competitionPath, isCompetitionOverviewPath } from '../../../utils/app-routes'

/** Owns state, effects and commands for CompetitionsByIdPage. */
export function useCompetitionsByIdPage() {
  const route = useRoute()
  const router = useRouter()

  const competitionId = computed(() => route.params.id as string)

  const isOverview = computed(() => isCompetitionOverviewPath(route.path))

  const isControlScreen = computed(() => [
    `/competitions/${competitionId.value}/live`,
    `/competitions/${competitionId.value}/awdp-live`,
  ].includes(route.path) || route.path.includes('/live-solo/program/') || route.path.includes('/live-solo/recordings/') || route.path.endsWith('/live-solo/settings') || route.path.endsWith('/live-solo/bracket') || route.path.includes('/live-solo/groups') || route.path.includes('/live-solo/postgame/'))

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

  const teamLoadError = ref<UiMessage | null>(null)

  const standingError = ref<UiMessage | null>(null)

  const loading = ref(true)

  const error = ref<UiMessage | null>(null)

  const { user, isAdministrator } = useAuth()

  const showCompetitionReturn = computed(() => {
    const base = competitionPath(competitionId.value)
    return [
      `${base}/progression`,
      `${base}/my/team`,
      `${base}/my/writeup`,
      `${base}/writeups`,
      `${base}/staff`,
    ].includes(route.path)
  })

  const returnsToAdministration = computed(() => (isAdministrator.value || competition.value?.administrationRole != null)
    && (isWriteUpReview.value || route.path === `${competitionPath(competitionId.value)}/staff`))
  const competitionReturnPath = computed(() => returnsToAdministration.value
    ? adminCompetitionPath(competitionId.value)
    : competitionPath(competitionId.value))
  const competitionReturnLabel = computed(() => returnsToAdministration.value
    ? 'competitions.label.backToAdministration' as const
    : 'common.label.backCompetition' as const)

  const { refreshMissedAnnouncements } = useCompetitionAnnouncementCatchUp(competitionId)

  const hasCompetitionStaffAccess = computed(() => competition.value?.administrationRole != null)

  const teamLoading = ref(false)
  let teamRequestId = 0
  const teamBanned = computed(() => myTeam.value?.isBanned === true && !hasCompetitionStaffAccess.value)

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
    const requestId = ++teamRequestId
    teamLoading.value = myTeam.value === null
    if (!user.value) {
      teamLoading.value = false
      myTeam.value = null
      myStanding.value = null
      teamLoadError.value = null
      standingError.value = null
      return
    }
    const { data, error: teamError, response } = await getMyTeamEndpoint({
      path: { competitionId: competitionId.value },
    })
    if (requestId !== teamRequestId) return
    teamLoading.value = false
    if (response?.status === 404) {
      myTeam.value = null
      teamLoadError.value = null
      standingError.value = null
      return
    }
    if (teamError || !data) {
      teamLoadError.value = parseApiError(teamError, describeMessage("competitions.competitionsBy.error.loadTeamFailed")).displayMessage
      return
    }
    teamLoadError.value = null
    myTeam.value = data
    await refreshMyStanding()
  }

  async function refreshMyStanding(): Promise<void> {
    if (competition.value?.mode === 'LiveSolo') {
      myStanding.value = null; standingError.value = null; standingLoading.value = false
      return
    }
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
      standingError.value = parseApiError(requestError, describeMessage("competitions.competitionsBy.error.loadTeamRankingFailed")).displayMessage
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
      error.value = parseApiError(err, describeMessage("common.error.loadingCompetitionFailed")).displayMessage
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
        if (event.kind === 'TeamBanned' || event.kind === 'TeamUnbanned' || event.kind === 'TeamBanCorrectionPublished' || event.kind === 'TeamMemberRemoved')
          void refreshMyTeam()
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
        if (!isWriteUpReview.value) void refreshMyTeam()
        if (!isWriteUpReview.value) void refreshStandingLatest()
        void refreshProgressionEnabled()
        void refreshMissedAnnouncements()
      },
    })
  })

  onUnmounted(() => {
    progressionRequestId++
    teamRequestId++
    unwatch?.()
  })

  provide(competitionContextKey, {
    competition,
    loading,
    error,
    refresh: async () => { await refresh() },
    standing: myStanding,
    refreshStanding: refreshMyStanding,
  })

  const navGroups = computed<WorkspaceNavGroup[]>(() => {
    const base = `/competitions/${competitionId.value}`
    const challengesVisible = competition.value?.mode !== 'LiveSolo' && (competition.value?.status === 'Running'
      || competition.value?.status === 'Paused'
      || competition.value?.status === 'Finished')
    const canReadChallenges = hasCompetitionStaffAccess.value || hasParticipantChallengeAccess.value
    return [
      {
        label: translate("common.label.competitions"),
        items: [
          { to: competitionPath(competitionId.value), label: translate("common.label.overview"), icon: LayoutDashboard, exact: true },
          ...(competition.value?.mode === 'LiveSolo' && canReadChallenges
            ? [{ to: `${base}/live-solo`, label: gameModeLabel('LiveSolo'), icon: GitBranch }] : []),
          ...(challengesVisible && canReadChallenges ? [{ to: `${base}/challenges`, label: translate("common.label.challenge.pageTitle"), icon: Puzzle }] : []),
          ...(competition.value?.mode === 'Ctf' && canReadChallenges && progressionEnabled.value
            ? [{ to: `${base}/progression`, label: translate('progression.title'), icon: GitBranch }]
            : []),
          ...(competition.value?.mode === 'LiveSolo' ? [] : [{ to: `${base}/leaderboard`, label: translate("common.label.leaderboard"), icon: Trophy }]),
          { to: `${base}/questions`, label: translate("common.label.questions"), icon: MessageCircleQuestion },
        ],
      },
      {
        label: translate("competitions.label.mine"),
        items: [
          { to: `${base}/my/team`, label: translate("competitions.label.myTeam"), icon: UserRound },
          ...(hasParticipantChallengeAccess.value
            ? [{ to: `${base}/my/writeup`, label: translate("writeUp.myWriteUp"), icon: FileText }]
            : []),
        ],
      },
      ...(hasCompetitionStaffAccess.value
        ? [{
            label: translate("common.label.management"),
            items: [
              { to: adminCompetitionPath(competitionId.value), label: translate('competitions.label.manageCompetition'), icon: LayoutDashboard },
              { to: `${base}/writeups`, label: translate("writeUp.review"), icon: ClipboardCheck },
              { to: `${base}/staff`, label: translate('staffWebhook.workbench'), icon: ClipboardCheck },
            ],
          }]
        : []),
    ]
  })

  provide(competitionWorkspaceNavigationKey, navGroups)

  const CompetitionTeamBanScreen = markRaw(CompetitionTeamBanScreenComponent)

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
      showCompetitionReturn,
      competitionReturnPath,
      competitionReturnLabel,
      ArrowLeft: markRaw(ArrowLeft),
      competition,
      competitionId, myTeam, teamBanned, teamLoading, CompetitionTeamBanScreen,
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
