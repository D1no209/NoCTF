import CompetitionTeamBanScreenComponent from '../../competition/CompetitionTeamBanScreen.vue'
import { message as describeMessage } from '../../../utils/i18n'
import type { UiMessage } from '../../../utils/i18n'
import { markRaw } from 'vue'

import { ArrowLeft, ClipboardCheck, FileText, GitBranch, LayoutDashboard, Monitor, MessageCircleQuestion, Puzzle, Trophy, UserRound } from '@lucide/vue'
import { getCompetitionEndpoint, getMyTeamStanding, getMyTeamEndpoint } from '../../../api'
import { watchNotifications } from '~/composables/useNotificationHub'
import type { NoCtfapiEndpointsCompetitionsCompetitionResponse, NoCtfapiEndpointsTeamsMyTeamStandingResponse, NoCtfapiEndpointsTeamsTeamResponse } from '../../../api'
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

  const isPublicSpectator = computed(() => /\/(awd-live|koh-live)$/.test(route.path))

  const isControlScreen = computed(() => [
    `/competitions/${competitionId.value}/live`,
    `/competitions/${competitionId.value}/awdp-live`,
    `/competitions/${competitionId.value}/awd-live`,
    `/competitions/${competitionId.value}/koh-live`,
  ].includes(route.path) || route.path.includes('/live-solo/program/') || route.path.includes('/live-solo/recordings/') || route.path.endsWith('/live-solo/settings') || route.path.endsWith('/live-solo/bracket') || route.path.includes('/live-solo/groups') || route.path.includes('/live-solo/question-pool') || route.path.includes('/live-solo/postgame/') || route.path.endsWith('/theory'))

  const isWriteUpReview = computed(() =>
    route.path === `/competitions/${competitionId.value}/writeups`,
  )

  const isProgression = computed(() =>
    route.path === `/competitions/${competitionId.value}/progression`,
  )

  const competition = ref<NoCtfapiEndpointsCompetitionsCompetitionResponse | null>(null)

  const myTeam = ref<NoCtfapiEndpointsTeamsTeamResponse | null>(null)

  const myStanding = ref<NoCtfapiEndpointsTeamsMyTeamStandingResponse | null>(null)

  const standingLoading = ref(false)

  const teamLoadError = ref<UiMessage | null>(null)

  const standingError = ref<UiMessage | null>(null)

  const loading = ref(true)

  const error = ref<UiMessage | null>(null)

  const { user, isAdministrator } = useAuth()
  const competitionActorId = ref<string | null>(null)
  let competitionRequestId = 0

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

  const canReadBroadcasts = computed(() => !!user.value
    && competitionActorId.value === user.value.userId
    && (hasCompetitionStaffAccess.value
      || !teamLoading.value && !teamLoadError.value && hasParticipantChallengeAccess.value))

  const progressionEnabled = computed(() => competition.value?.features?.progressionEnabled === true
    && competition.value.features.progressionMapVisible === true)
  const theoryVisible = computed(() => !!user.value && user.value.kind === 'Human'
    && hasParticipantChallengeAccess.value && ['Ctf', 'Awd', 'Awdp'].includes(competition.value?.mode ?? '')
    && competition.value?.features?.hasPublishedTheory === true)
  let unwatchTheory: (() => void) | undefined
  onMounted(() => { unwatchTheory = watchNotifications({ theoryChanged: () => void refresh(), onReconnected: () => void refresh() }) })
  onBeforeUnmount(() => unwatchTheory?.())

  let standingGeneration = 0
  let standingPending: Promise<boolean> | null = null
  const standingIdentity = computed(() => `${user.value?.userId ?? ''}:${competitionId.value}:${myTeam.value?.id ?? ''}:${myTeam.value?.registrationStatus ?? ''}:${myTeam.value?.isBanned ?? false}:${hasCompetitionStaffAccess.value ? 'Staff' : competition.value?.leaderboardVisibility ?? ''}`)
  watch(standingIdentity, () => {
    standingGeneration++; standingPending = null; standingPolling.stop()
    myStanding.value = null; standingError.value = null; standingLoading.value = false
    void refreshMyStanding()
  }, { flush: 'sync' })

  async function refreshMyTeam() {
    if (isPublicSpectator.value) return
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
    if ([401, 403, 404].includes(response?.status ?? 0)) {
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
    void refreshMyStanding()
  }

  function loadStanding(): Promise<boolean> {
    if (standingPending) return standingPending
    if (isPublicSpectator.value || !user.value || competition.value?.mode === 'LiveSolo'
      || myTeam.value?.registrationStatus !== 'Approved' || myTeam.value.isBanned || !myTeam.value.id) {
      myStanding.value = null; standingError.value = null; standingLoading.value = false
      return Promise.resolve(true)
    }
    const generation = standingGeneration, teamId = myTeam.value.id
    let processing = false
    standingLoading.value = myStanding.value === null
    const task = (async () => {
      try {
        const { data, error: requestError, response } = await getMyTeamStanding({ path: { competitionId: competitionId.value } })
        if (generation !== standingGeneration) return true
        if ([401, 403, 404].includes(response?.status ?? 0)) {
          myStanding.value = null; standingError.value = null; return true
        }
        if (response?.status === 202) { processing = true; standingError.value = null; return false }
        if (requestError || !data || !('teamId' in data)) throw requestError
        if (data.teamId !== teamId) { myStanding.value = null; return true }
        standingError.value = null
        myStanding.value = data.dataScope === 'Hidden' ? null : data
        return true
      } catch (cause) {
        if (generation === standingGeneration)
          standingError.value = parseApiError(cause, describeMessage('competitions.competitionsBy.error.loadTeamRankingFailed')).displayMessage
        return true
      } finally {
        if (generation === standingGeneration) standingLoading.value = processing && myStanding.value === null
      }
    })().finally(() => { if (standingPending === task) standingPending = null })
    standingPending = task
    return task
  }
  const standingPolling = usePolling(loadStanding, { interval: 2000, maxInterval: 8000, timeout: 120_000 })
  watch(standingPolling.timedOut, value => {
    if (value) { standingLoading.value = false; standingError.value = describeMessage('competitions.competitionsBy.error.loadTeamRankingFailed') }
  })
  async function refreshMyStanding(): Promise<void> {
    if (!await loadStanding()) standingPolling.start()
  }

  const refreshStandingLatest = createTrailingRefresh(refreshMyStanding)

  async function refresh(): Promise<'loaded' | 'not-found' | 'failed'> {
    if (isPublicSpectator.value) return 'failed'
    const requestId = ++competitionRequestId
    const actorId = user.value?.userId ?? null
    const { data, error: err, response } = await getCompetitionEndpoint({
      path: { competitionId: competitionId.value },
    })
    if (requestId !== competitionRequestId || (user.value?.userId ?? null) !== actorId) return 'failed'
    loading.value = false
    if (err || !data) {
      competitionActorId.value = null
      error.value = parseApiError(err, describeMessage("common.error.loadingCompetitionFailed")).displayMessage
      if ([401, 403, 404].includes(response?.status ?? 0)) {
        competition.value = null; myTeam.value = null; myStanding.value = null
      }
      return response?.status === 404 ? 'not-found' : 'failed'
    }
    error.value = null
    competition.value = data
    competitionActorId.value = actorId
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
      if (isOverview.value || isPublicSpectator.value) return
      competitionActorId.value = null
      competition.value = null
      myTeam.value = null
      myStanding.value = null
      void refresh()
      if (!isWriteUpReview.value) void refreshMyTeam()
      void refreshMissedAnnouncements()
    },
  )

  let unwatch: (() => void) | undefined

  function subscribeWorkspace() {
    unwatch?.()
    unwatch = undefined
    if (isOverview.value || isPublicSpectator.value) return
    unwatch = watchCompetition(competitionId.value, {
      competitionLifecycleChanged: () => {
        void refresh()
        if (!isWriteUpReview.value) void refreshStandingLatest()
      },
      competitionEventChanged: event => {
        if (isPublicSpectator.value) return
        if (event.kind === 'TeamBanned' || event.kind === 'TeamUnbanned' || event.kind === 'TeamBanCorrectionPublished'
          || event.kind === 'TeamRegistered' || event.kind === 'TeamRegistrationChanged' || event.kind === 'TeamDeleted'
          || event.kind === 'TeamMemberJoined' || event.kind === 'TeamMemberRemoved')
          void refreshMyTeam()
        if (event.kind === 'CompetitionAudienceChanged' || event.kind === 'LeaderboardVisibilityChanged'
          || event.kind === 'TrackConfigurationUpdated' || event.kind === 'TeamTrackChanged') {
          standingGeneration++; standingPending = null; myStanding.value = null; standingPolling.stop()
          void handleAudienceChanged()
        }
        if (event.kind === 'CompetitionUpdated') {
          void refresh()
        }
        if (event.kind === 'AnnouncementPublished')
          void refreshMissedAnnouncements()
      },
      scoreboardUpdated: () => {
        if (!isWriteUpReview.value) void refreshStandingLatest()
      },
      onReconnected: () => {
        if (isPublicSpectator.value) return
        standingGeneration++; standingPending = null; myStanding.value = null; standingPolling.stop()
        if (!isWriteUpReview.value) void refreshMyTeam()
        if (!isWriteUpReview.value) void refreshStandingLatest()
        void refresh()
        void refreshMissedAnnouncements()
      },
    })
  }
  onMounted(subscribeWorkspace)
  watch(isPublicSpectator, enabled => {
    if (enabled) {
      unwatch?.(); unwatch = undefined
      competitionRequestId++; teamRequestId++; standingGeneration++; standingPolling.stop()
      competition.value = null; competitionActorId.value = null; myTeam.value = null; myStanding.value = null
    } else {
      subscribeWorkspace()
      void initialize()
    }
  }, { flush: 'sync' })

  onUnmounted(() => {
    competitionRequestId++
    standingGeneration++; standingPolling.stop()
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
    canReadBroadcasts,
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
          ...(theoryVisible.value ? [{ to: `${base}/theory`, label: translate('common.label.theoryActivities'), icon: FileText }] : []),
          ...(competition.value?.mode === 'Ctf' && canReadChallenges && progressionEnabled.value
            ? [{ to: `${base}/progression`, label: translate('progression.title'), icon: GitBranch }]
            : []),
          ...(competition.value?.mode === 'LiveSolo' ? [] : [{ to: `${base}/leaderboard`, label: translate("common.label.leaderboard"), icon: Trophy }]),
          ...(competition.value?.mode === 'Awd' || competition.value?.mode === 'Koh'
            ? [{ to: `${base}/${competition.value.mode === 'Awd' ? 'awd' : 'koh'}-live`, label: translate('competitions.label.spectator'), icon: Monitor }] : []),
          { to: `${base}/questions`, label: translate("common.label.questions"), icon: MessageCircleQuestion },
        ],
      },
      {
        label: translate("competitions.label.mine"),
        items: [
          { to: `${base}/my/team`, label: translate("competitions.label.myTeam"), icon: UserRound },
          ...(hasParticipantChallengeAccess.value
            ? [{ to: competition.value?.mode === 'LiveSolo' ? `${base}/live-solo/writeups` : `${base}/my/writeup`, label: translate(competition.value?.mode === 'LiveSolo' ? 'liveSolo.postgame.title' : 'writeUp.myWriteUp'), icon: FileText }]
            : []),
        ],
      },
      ...(hasCompetitionStaffAccess.value
        ? [{
            label: translate("common.label.management"),
            items: competition.value?.mode === 'LiveSolo' ? [
              { to: `/admin/competitions/${competitionId.value}/live-solo`, label: translate('liveSolo.navigation.manage'), icon: LayoutDashboard },
            ] : [
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
    if (isOverview.value || isPublicSpectator.value) return
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
