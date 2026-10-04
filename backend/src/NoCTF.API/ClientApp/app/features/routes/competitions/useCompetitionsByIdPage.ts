import { ProjectionResponseOption } from '../../../lib/api'
import { createNoCTFAPIEndpointsCompetitionsScoreboardSnapshotResponseFromDiscriminatorValue } from '../../../api/models'

import { ResponseMetadata, RequestPolicyOption } from '../../../lib/api'

import { api } from '../../../lib/api'
import { message as describeMessage } from '../../../utils/i18n'
import type { UiMessage } from '../../../utils/i18n'
import { markRaw } from 'vue'

import { ClipboardCheck, FileText, GitBranch, LayoutDashboard, MessageCircleQuestion, Puzzle, Trophy, UserRound } from '@lucide/vue'

import type { NoCTFAPIEndpointsCompetitionsCompetitionResponse, NoCTFAPIEndpointsCompetitionsScoreboardTeamResponse, NoCTFAPIEndpointsTeamsTeamResponse } from '../../../api/models'
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

  const competition = ref<NoCTFAPIEndpointsCompetitionsCompetitionResponse | null>(null)

  const myTeam = ref<NoCTFAPIEndpointsTeamsTeamResponse | null>(null)

  const myStanding = ref<NoCTFAPIEndpointsCompetitionsScoreboardTeamResponse | null>(null)

  const standingLoading = ref(false)

  const teamLoadError = ref<UiMessage | null>(null)

  const standingError = ref<UiMessage | null>(null)

  const loading = ref(true)

  const error = ref<UiMessage | null>(null)

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
    const data = await api.api.v1.competitions.byCompetitionId(competitionId.value).progression.get();
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
    let teamError: unknown;
    const response = new ResponseMetadata();
    const data = await api.api.v1.competitions.byCompetitionId(competitionId.value).teams.me.get({ options: [new RequestPolicyOption({ response: response })] }).catch(cause => { teamError = cause; return undefined });
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
    if (myTeam.value?.registrationStatus !== 'Approved' || myTeam.value.isBanned || !myTeam.value.id) {
      myStanding.value = null
      standingLoading.value = false
      standingError.value = null
      return
    }
    standingLoading.value = myStanding.value === null
    let requestError: unknown;
    const response = new ResponseMetadata();
    const data = await api.api.v1.competitions.byCompetitionId(competitionId.value).leaderboard.get({ queryParameters: { endingRound: undefined } , options: [new RequestPolicyOption({ response: response }), new ProjectionResponseOption(createNoCTFAPIEndpointsCompetitionsScoreboardSnapshotResponseFromDiscriminatorValue)] }).catch(cause => { requestError = cause; return undefined });
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
    let err: unknown;
    const response = new ResponseMetadata();
    const data = await api.api.v1.competitions.byCompetitionId(competitionId.value).get({ options: [new RequestPolicyOption({ response: response })] }).catch(cause => { err = cause; return undefined });
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
        label: translate("common.label.competitions"),
        items: [
          { to: competitionPath(competitionId.value), label: translate("common.label.overview"), icon: LayoutDashboard, exact: true },
          ...(challengesVisible && canReadChallenges ? [{ to: `${base}/challenges`, label: translate("common.label.challenge.pageTitle"), icon: Puzzle }] : []),
          ...(competition.value?.mode === 'Ctf' && canReadChallenges && progressionEnabled.value
            ? [{ to: `${base}/progression`, label: translate('progression.title'), icon: GitBranch }]
            : []),
          { to: `${base}/leaderboard`, label: translate("common.label.leaderboard"), icon: Trophy },
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
