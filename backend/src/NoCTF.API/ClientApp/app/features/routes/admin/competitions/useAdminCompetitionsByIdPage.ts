import { message as describeMessage } from '../../../../utils/i18n'
import type { UiMessage } from '../../../../utils/i18n'
import { adminWorkspacePath, competitionChallengeManagementPath } from '~/features/admin/admin-navigation'
import { competitionContextKey } from '~/utils/labels'
import { isLiveSoloManagementPath, liveSoloManagementPath, liveSoloPlayerPath } from '~/features/live-solo/navigation'
import { markRaw } from 'vue'

import { Activity, ChartNoAxesCombined, ClipboardCheck, Container, Download, FileCheck, GitBranch, KeyRound, LayoutDashboard, Mail, Network, Orbit, Puzzle, Settings, ShieldAlert, Trophy, Users, Webhook } from '@lucide/vue'
import type { WorkspaceNavGroup } from '../../../app/workspace-nav'
import { CompetitionAdminKey } from '../../../../lib/admin-competition'
import type { CompetitionAdminRole } from '../../../../lib/admin-competition'
import CompetitionStatusBadgeComponent from '../../../admin/CompetitionStatusBadge.vue'
import GameModeBadgeComponent from '../../../admin/GameModeBadge.vue'
import AppWorkspaceNavComponent from '../../../app/AppWorkspaceNav.vue'
import { useAdminCompetitionResource } from '../../../admin/useAdminCompetitionResource'

/** Owns state, effects and commands for AdminCompetitionsByIdPage. */
export function useAdminCompetitionsByIdPage() {
  const route = useRoute()

  const activePath = computed(() => adminWorkspacePath(route.path))

  const competitionId = route.params.id as string

  const base = `/admin/competitions/${competitionId}`

  const isProgressionPage = computed(() => activePath.value ===
    `${base}/progression`)

  const isWriteUpReview = computed(() => activePath.value === `${base}/writeups`)
  const isSoloWorkspace = computed(() => isLiveSoloManagementPath(route.path))
  const playerPath = computed(() => typeof route.params.matchId === 'string'
    ? `${liveSoloPlayerPath(competitionId)}/matches/${encodeURIComponent(route.params.matchId)}`
    : { path: liveSoloPlayerPath(competitionId), query: typeof route.query?.match === 'string' ? { match: route.query.match } : {} })
  const playerViewLabel = computed(() => typeof route.params.matchId === 'string' ? 'liveSolo.navigation.playerMatchView' as const : 'liveSolo.navigation.playerView' as const)

  const usesPageScroll = computed(() => activePath.value === `${base}/teams`
    || activePath.value.startsWith(`${base}/teams/`)
    || activePath.value === `${base}/challenges`
    || activePath.value.startsWith(`${base}/challenges/`)
    || isProgressionPage.value || isSoloWorkspace.value)

  const { user, isAdministrator } = useAuth()

  const resource = useAdminCompetitionResource(competitionId)
  const competition = resource.competition

  const role = ref<CompetitionAdminRole>('Observer')

  const loading = ref(true)

  const error = ref<UiMessage | null>(null)

  const canWrite = computed(() => resource.fresh.value && (role.value === 'Owner' || role.value === 'Manager'))

  const canJudge = computed(() => resource.fresh.value && role.value !== 'Observer')

  const canManagePermissions = computed(() => resource.fresh.value && role.value === 'Owner')

  const RoleLabel: Record<CompetitionAdminRole, string> = {
    Owner: "common.label.owner",
    Manager: "administration.label.administrator",
    Judge: "common.label.judge",
    Observer: "common.label.observer",
  }

  async function load(force = false) {
    const actor = user.value?.userId
    try {
      const result = await (force ? resource.refresh() : resource.read())
      if (actor !== user.value?.userId) return
      error.value = result.error ? parseApiError(result.error, describeMessage('common.error.loadingCompetitionFailed')).displayMessage : null
      await resolveRole()
      if ([401, 403, 404].includes(result.response?.status ?? 0)) {
        await navigateTo(result.response?.status === 401 ? '/auth/login' : `/competitions/${competitionId}`)
      }
    } catch (cause) {
      error.value = parseApiError(cause, describeMessage('common.error.loadingCompetitionFailed')).displayMessage
    }
  }
  async function refresh() { await load(true) }

  async function resolveRole() {
    if (isAdministrator.value || (user.value?.userId && competition.value?.ownerId === user.value.userId)) {
      role.value = 'Owner'
      return
    }
    const protocolRole = competition.value?.administrationRole
    role.value = protocolRole ?? 'Observer'
  }

  watch(competition, () => { void resolveRole() }, { immediate: true, flush: 'sync' })
  watch(resource.error, cause => { error.value = cause ? parseApiError(cause, describeMessage('common.error.loadingCompetitionFailed')).displayMessage : null })

  provide(CompetitionAdminKey, {
    competitionId,
    data: resource.data, read: resource.read, accept: resource.accept,
    competition,
    role,
    canWrite,
    canJudge,
    canManagePermissions,
    refresh,
  })
  provide(competitionContextKey, {
    competition: computed(() => competition.value ? { ...competition.value, administrationRole: resource.fresh.value ? role.value : 'Observer' } : null),
    managementFresh: resource.fresh,
    loading, error, refresh,
    standing: ref(null), refreshStanding: async () => {},
    canReadBroadcasts: computed(() => competition.value != null && error.value == null),
  })

  const soloNavGroups = computed<WorkspaceNavGroup[]>(() => [
    {
      label: translate('liveSolo.navigation.operations'),
      items: [
        { to: base, label: translate('liveSolo.navigation.overview'), icon: LayoutDashboard, exact: true },
        { to: liveSoloManagementPath(competitionId), label: translate('liveSolo.navigation.matches'), icon: GitBranch },
        { to: `${base}/teams`, label: translate('common.label.teamManagement'), icon: Users },
        ...(canWrite.value ? [
          { to: `${base}/configuration`, label: translate('liveSolo.navigation.competitionSettings'), icon: Settings },
          { to: `${liveSoloManagementPath(competitionId)}/settings`, label: translate('liveSolo.navigation.modeSettings'), icon: Settings },
          { to: `${base}/tracks`, label: translate('common.label.tracks'), icon: GitBranch },
          { to: `${base}/directions`, label: translate('directionSettings.title'), icon: Puzzle },
          { to: `${base}/webhooks`, label: translate('webhook.title'), icon: Webhook },
          ...(canManagePermissions.value ? [{ to: `${base}/permissions`, label: translate('administration.label.permissions'), icon: KeyRound }] : []),
        ] : []),
      ],
    },
    {
      label: translate('liveSolo.navigation.authoring'),
      items: [
        { to: competitionChallengeManagementPath(competitionId, 'LiveSolo'), label: translate('liveSolo.pool.title'), icon: Puzzle },
        { to: `${liveSoloManagementPath(competitionId)}/groups`, label: translate('liveSolo.groups.title'), icon: Puzzle },
        { to: `${liveSoloManagementPath(competitionId)}/bracket`, label: translate('liveSolo.bracket.title'), icon: GitBranch },
      ],
    },
    {
      label: translate('liveSolo.navigation.review'),
      items: [
        { to: `${liveSoloManagementPath(competitionId)}/events`, label: translate('administration.label.activity'), icon: Activity },
        { to: `${base}/submissions`, label: translate('common.label.submissions'), icon: FileCheck },
        { to: `${base}/runtimes`, label: translate('administration.label.runtime'), icon: Container },
        { to: `${liveSoloManagementPath(competitionId)}/writeups`, label: translate(canJudge.value ? 'liveSolo.navigation.postgameReview' : 'liveSolo.postgame.title'), icon: ClipboardCheck },
        { to: `${liveSoloManagementPath(competitionId)}/staff`, label: translate('liveSolo.navigation.staffTasks'), icon: ClipboardCheck },
        ...(canJudge.value ? [
          { to: `${base}/cheats`, label: translate('administration.label.cheating'), icon: ShieldAlert },
          { to: `${base}/traffic-captures`, label: translate('runtime.trafficCaptures'), icon: Network },
        ] : []),
        ...(canJudge.value ? [{ to: `${base}/announcements`, label: translate('announcements.title'), icon: Mail }] : []),
        ...(canWrite.value ? [
          { to: `${base}/exports`, label: translate('administration.label.export'), icon: Download },
        ] : []),
      ],
    },
  ].filter(group => group.items.length > 0))

  const navGroups = computed<WorkspaceNavGroup[]>(() => competition.value?.mode === 'LiveSolo' ? soloNavGroups.value : [

    {
      label: translate("administration.label.operations"),
      items: [
        { to: base, label: translate("common.label.overview"), icon: LayoutDashboard, exact: true },
        ...(canWrite.value ? [
          { to: `${base}/configuration`, label: translate('administration.label.configuration'), icon: Settings },
          { to: `${base}/tracks`, label: translate("common.label.tracks"), icon: GitBranch },
          { to: `${base}/directions`, label: translate('directionSettings.title'), icon: Puzzle },
        ] : []),
        { to: competitionChallengeManagementPath(competitionId, competition.value?.mode), label: translate('common.label.challenge.pageTitle'), icon: Puzzle },
        { to: `${base}/teams`, label: translate("common.label.teamManagement"), icon: Users },
        ...(['Ctf', 'Awd', 'Awdp'].includes(competition.value?.mode ?? '')
          ? [{ to: `${base}/theory`, label: translate('common.label.theoryActivities'), icon: FileCheck }] : []),
      ],
    },
    {
      label: translate("administration.label.monitoring"),
      items: [
        ...(competition.value?.mode === 'Awdp'
          ? [{ to: `/competitions/${competitionId}/awdp-live`, label: translate("administration.label.controlScreen"), icon: Orbit }]
          : competition.value?.mode === 'Awd' || competition.value?.mode === 'Koh'
            ? [{ to: `/competitions/${competitionId}/${competition.value.mode === 'Awd' ? 'awd' : 'koh'}-live`, label: translate("administration.label.controlScreen"), icon: Orbit }]
          : competition.value?.mode === 'Ctf' ? [{ to: `/competitions/${competitionId}/live`, label: translate("leaderboard.ctf.liveTitle"), icon: Orbit }] : []),
        { to: `/competitions/${competitionId}/events`, label: translate("administration.label.activity"), icon: Activity },
        { to: `${base}/submissions`, label: translate("common.label.submissions"), icon: FileCheck },
        { to: `${base}/runtimes`, label: translate("administration.label.runtime"), icon: Container },
        ...(canJudge.value ? [
          { to: `${base}/traffic-captures`, label: translate("runtime.trafficCaptures"), icon: Network },
          { to: `${base}/cheats`, label: translate("administration.label.cheating"), icon: ShieldAlert },
        ] : []),
        ...[
          ...(canWrite.value ? [{ to: `${base}/leaderboard`, label: translate("common.label.leaderboard"), icon: Trophy }] : []),
          ...(!canWrite.value ? [{ to: `/competitions/${competitionId}/leaderboard`, label: translate('leaderboard.label.viewScoreboard'), icon: ChartNoAxesCombined }] : []),
        ],
      ],
    },
    {
      label: translate("common.label.management"),
      items: [
        ...(canJudge.value ? [{ to: `${base}/announcements`, label: translate('announcements.title'), icon: Mail }] : []),
        { to: `${base}/writeups`, label: translate(canJudge.value ? 'writeUp.review' : 'administration.navigation.viewWriteups'), icon: ClipboardCheck },
        ...(canWrite.value ? [
          { to: `${base}/exports`, label: translate("administration.label.export"), icon: Download },
          { to: `${base}/webhooks`, label: translate("webhook.title"), icon: Webhook },
        ] : []),
        ...(canWrite.value && competition.value?.mode === 'Ctf'
          ? [{ to: `${base}/progression`, label: translate('progression.title'), icon: GitBranch }]
          : []),
        ...(canManagePermissions.value
          ? [{ to: `${base}/permissions`, label: translate("administration.label.permissions"), icon: KeyRound }]
          : []),
      ],
    },
  ])

  onMounted(async () => {
    loading.value = true
    await load()
    loading.value = false
  })

  let unsubscribe: (() => void) | undefined
  onMounted(() => {
    unsubscribe = watchCompetition(competitionId, {
      competitionEventChanged: event => {
        if (event.kind === 'CompetitionUpdated' || event.kind === 'CompetitionAudienceChanged'
          || event.kind === 'TrackConfigurationUpdated' || event.kind === 'TrackRegistrationPolicyUpdated'
          || event.kind === 'LeaderboardVisibilityChanged' || event.kind === 'AwdScoringRulesChanged') void refresh()
      },
      competitionLifecycleChanged: () => void refresh(),
      onReconnected: () => void refresh(),
    })
  })
  watch(() => user.value?.userId, () => { resource.clear(); void navigateTo('/competitions') }, { flush: 'sync' })
  onUnmounted(() => { unsubscribe?.(); resource.clear() })

  const CompetitionStatusBadge = markRaw(CompetitionStatusBadgeComponent)

  const GameModeBadge = markRaw(GameModeBadgeComponent)

  const AppWorkspaceNav = markRaw(AppWorkspaceNavComponent)

  const viewBindings = {
      Mail,
      competition,
      role,
      loading,
      error, refresh,
      RoleLabel,
      navGroups,
      activePath,
      isProgressionPage,
      isWriteUpReview,
      usesPageScroll,
      isSoloWorkspace, playerPath, playerViewLabel,
      CompetitionStatusBadge,
      GameModeBadge,
      AppWorkspaceNav
    }
  return viewBindings
}

export type AdminCompetitionsByIdPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useAdminCompetitionsByIdPage>>>
