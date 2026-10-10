import { message as describeMessage } from '../../../../utils/i18n'
import type { UiMessage } from '../../../../utils/i18n'
import { adminWorkspacePath } from '~/features/admin/admin-navigation'
import { markRaw } from 'vue'

import { Activity, ChartNoAxesCombined, ClipboardCheck, Container, Download, FileCheck, GitBranch, KeyRound, LayoutDashboard, Mail, Network, Orbit, Puzzle, Settings, ShieldAlert, Trophy, Users, Webhook } from '@lucide/vue'
import { adminGetCompetition } from '../../../../api'
import type { NoCtfapiEndpointsCompetitionsCompetitionResponse } from '../../../../api'
import type { WorkspaceNavGroup } from '../../../app/workspace-nav'
import { CompetitionAdminKey } from '../../../../lib/admin-competition'
import type { CompetitionAdminRole } from '../../../../lib/admin-competition'
import CompetitionStatusBadgeComponent from '../../../admin/CompetitionStatusBadge.vue'
import GameModeBadgeComponent from '../../../admin/GameModeBadge.vue'
import AppWorkspaceNavComponent from '../../../app/AppWorkspaceNav.vue'

/** Owns state, effects and commands for AdminCompetitionsByIdPage. */
export function useAdminCompetitionsByIdPage() {
  const route = useRoute()

  const activePath = computed(() => adminWorkspacePath(route.path))

  const competitionId = route.params.id as string

  const base = `/admin/competitions/${competitionId}`

  const isProgressionPage = computed(() => activePath.value ===
    `${base}/progression`)

  const isWriteUpReview = computed(() => activePath.value === `${base}/writeups`)

  const usesPageScroll = computed(() => activePath.value === `${base}/teams`
    || activePath.value.startsWith(`${base}/teams/`)
    || activePath.value === `${base}/challenges`
    || activePath.value.startsWith(`${base}/challenges/`)
    || isProgressionPage.value)

  const { user, isAdministrator } = useAuth()

  const competition = ref<NoCtfapiEndpointsCompetitionsCompetitionResponse | null>(null)

  const role = ref<CompetitionAdminRole>('Observer')

  const loading = ref(true)

  const error = ref<UiMessage | null>(null)

  const canWrite = computed(() => role.value === 'Owner' || role.value === 'Manager')

  const canJudge = computed(() => role.value !== 'Observer')

  const canManagePermissions = computed(() => role.value === 'Owner')

  const RoleLabel: Record<CompetitionAdminRole, string> = {
    Owner: "common.label.owner",
    Manager: "administration.label.administrator",
    Judge: "common.label.judge",
    Observer: "common.label.observer",
  }

  async function refresh() {
    const { data, error: e } = await adminGetCompetition({ path: { competitionId } })
    if (e || !data) {
      competition.value = null
      role.value = 'Observer'
      error.value = parseApiError(e, describeMessage("common.error.loadingCompetitionFailed")).displayMessage
      return
    }
    competition.value = data.competition ?? null
    await resolveRole()
    error.value = null
  }

  async function resolveRole() {
    if (isAdministrator.value || (user.value?.userId && competition.value?.ownerId === user.value.userId)) {
      role.value = 'Owner'
      return
    }
    const protocolRole = competition.value?.administrationRole
    role.value = protocolRole ?? 'Observer'
  }

  provide(CompetitionAdminKey, {
    competitionId,
    competition,
    role,
    canWrite,
    canJudge,
    canManagePermissions,
    refresh,
  })

  const navGroups = computed<WorkspaceNavGroup[]>(() => [
    {
      label: translate("administration.label.operations"),
      items: [
        { to: base, label: translate("common.label.overview"), icon: LayoutDashboard, exact: true },
        ...(canWrite.value ? [
          { to: `${base}/configuration`, label: translate("administration.label.configuration"), icon: Settings },
          { to: `${base}/tracks`, label: translate("common.label.tracks"), icon: GitBranch },
          { to: `${base}/directions`, label: translate('directionSettings.title'), icon: Puzzle },
        ] : []),
        { to: `${base}/challenges`, label: translate("common.label.challenge.pageTitle"), icon: Puzzle },
        { to: `${base}/teams`, label: translate("common.label.teamManagement"), icon: Users },
      ],
    },
    ...(competition.value?.mode === 'LiveSolo' ? [{
      label: gameModeLabel('LiveSolo'),
      items: [
        { to: `/competitions/${competitionId}/live-solo`, label: translate('liveSolo.hall'), icon: GitBranch },
        ...(canWrite.value ? [
          { to: `/competitions/${competitionId}/live-solo/settings`, label: translate('liveSolo.settings.title'), icon: Settings },
          { to: `/competitions/${competitionId}/live-solo/groups`, label: translate('liveSolo.groups.title'), icon: Puzzle },
          { to: `/competitions/${competitionId}/live-solo/bracket`, label: translate('liveSolo.bracket.title'), icon: GitBranch },
        ] : []),
      ],
    }] : []),
    {
      label: translate("administration.label.monitoring"),
      items: [
        ...(competition.value?.mode === 'Awdp'
          ? [{ to: `/competitions/${competitionId}/awdp-live`, label: translate("administration.label.controlScreen"), icon: Orbit }]
          : competition.value?.mode === 'Ctf' ? [{ to: `/competitions/${competitionId}/live`, label: translate("leaderboard.ctf.liveTitle"), icon: Orbit }] : []),
        { to: `/competitions/${competitionId}/events`, label: translate("administration.label.activity"), icon: Activity },
        { to: `${base}/submissions`, label: translate("common.label.submissions"), icon: FileCheck },
        { to: `${base}/runtimes`, label: translate("administration.label.runtime"), icon: Container },
        ...(canJudge.value ? [
          { to: `${base}/traffic-captures`, label: translate("runtime.trafficCaptures"), icon: Network },
          { to: `${base}/cheats`, label: translate("administration.label.cheating"), icon: ShieldAlert },
        ] : []),
        ...(competition.value?.mode === 'LiveSolo' ? [] : [
          ...(canWrite.value ? [{ to: `${base}/leaderboard`, label: translate("common.label.leaderboard"), icon: Trophy }] : []),
          ...(!canWrite.value ? [{ to: `/competitions/${competitionId}/leaderboard`, label: translate('leaderboard.label.viewScoreboard'), icon: ChartNoAxesCombined }] : []),
        ]),
      ],
    },
    {
      label: translate("common.label.management"),
      items: [
        ...(canWrite.value ? [{ to: `${base}/announcements`, label: translate('announcements.title'), icon: Mail }] : []),
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
    await refresh()
    loading.value = false
  })

  const CompetitionStatusBadge = markRaw(CompetitionStatusBadgeComponent)

  const GameModeBadge = markRaw(GameModeBadgeComponent)

  const AppWorkspaceNav = markRaw(AppWorkspaceNavComponent)

  const viewBindings = {
      Mail,
      competition,
      role,
      loading,
      error,
      RoleLabel,
      navGroups,
      activePath,
      isProgressionPage,
      isWriteUpReview,
      usesPageScroll,
      CompetitionStatusBadge,
      GameModeBadge,
      AppWorkspaceNav
    }
  return viewBindings
}

export type AdminCompetitionsByIdPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useAdminCompetitionsByIdPage>>>
