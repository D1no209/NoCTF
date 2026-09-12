import { markRaw } from 'vue'

import { ClipboardCheck, FileText, LayoutDashboard, MessageCircleQuestion, Puzzle, Trophy, UserRound } from '@lucide/vue'
import { getCompetitionEndpoint, getLeaderboardEndpoint, getMyTeamEndpoint } from '../../../api'
import type { NoCtfapiEndpointsCompetitionsCompetitionResponse, NoCtfapiEndpointsCompetitionsScoreboardTeamResponse, NoCtfapiEndpointsTeamsTeamResponse } from '../../../api'
import { competitionWorkspaceNavigationKey } from '../../app/workspace-nav'
import type { WorkspaceNavGroup } from '../../app/workspace-nav'
import { createTrailingRefresh } from '../../../lib/latest-page-refresh'
import CompetitionCountdownComponent from '../../competitions/CompetitionCountdown.vue'
import LifecycleBadgeComponent from '../../competitions/LifecycleBadge.vue'

/** Owns state, effects and commands for CompetitionsByIdPage. */
export function useCompetitionsByIdPage() {
  const route = useRoute()
  const router = useRouter()

  const competitionId = computed(() => route.params.id as string)

  const isControlScreen = computed(() => [
    `/competitions/${competitionId.value}/live`,
    `/competitions/${competitionId.value}/live-`,
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
    if (result === 'loaded')
      await refreshMyTeam()
  }

  watch(
    () => user.value?.userId,
    () => void refreshMyTeam(),
  )

  let unwatch: (() => void) | undefined

  onMounted(() => {
    unwatch = watchCompetition(competitionId.value, {
      competitionLifecycleChanged: () => {
        void refresh()
        void refreshStandingLatest()
      },
      competitionEventChanged: event => {
        if (event.kind === 'CompetitionAudienceChanged')
          void handleAudienceChanged()
      },
      scoreboardUpdated: () => void refreshStandingLatest(),
      onReconnected: () => void refreshStandingLatest(),
    })
  })

  onUnmounted(() => unwatch?.())

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
          { to: `/competitions?competition=${competitionId.value}`, label: translate("ui.overview"), icon: LayoutDashboard, exact: true },
          ...(challengesVisible && canReadChallenges ? [{ to: `${base}/challenges`, label: translate("ui.challenge"), icon: Puzzle }] : []),
          { to: `${base}/leaderboard`, label: translate("ui.leaderboard"), icon: Trophy },
          { to: `${base}/questions`, label: translate("ui.questions"), icon: MessageCircleQuestion },
        ],
      },
      {
        label: translate("ui.mine"),
        items: [
          { to: `${base}/my/team`, label: translate("ui.myTeam"), icon: UserRound },
          ...(hasParticipantChallengeAccess.value
            && competition.value?.writeUpSubmissionRequired === true
            ? [{ to: `${base}/my/writeup`, label: translate("writeUp.myWriteUp"), icon: FileText }]
            : []),
        ],
      },
      ...(hasCompetitionStaffAccess.value
        && competition.value?.writeUpSubmissionRequired === true
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
    await refresh()
    await refreshMyTeam()
  }

  return {
      initialize,
      isControlScreen,
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
