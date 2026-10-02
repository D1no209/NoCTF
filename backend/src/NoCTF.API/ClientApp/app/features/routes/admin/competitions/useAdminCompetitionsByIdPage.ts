import { proxyRefs } from 'vue'
import { adminWorkspacePath } from '~/features/admin/admin-navigation'
import { markRaw } from 'vue'

import { Activity, ClipboardCheck, Container, Download, FileCheck, GitBranch, KeyRound, LayoutDashboard, Megaphone, Network, Orbit, Puzzle, Settings, ShieldAlert, Trophy, Users, Webhook } from '@lucide/vue'
import { toast } from 'vue-sonner'
import { adminCreateCompetitionAnnouncement, adminGetCompetition } from '../../../../api'
import type { NoCtfapiEndpointsAdministrationCompetitionsAnnouncementAudience, NoCtfapiEndpointsCompetitionsCompetitionResponse } from '../../../../api'
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

  const error = ref<string | null>(null)

  const canWrite = computed(() => role.value === 'Owner' || role.value === 'Manager')

  const canJudge = computed(() => role.value !== 'Observer')

  const canManagePermissions = computed(() => role.value === 'Owner')

  const canAnnounce = computed(() => role.value !== 'Observer')

  const announcementOpen = ref(false)

  const announcementTitle = ref('')

  const announcementBody = ref('')

  const announcementAudience = ref<NoCtfapiEndpointsAdministrationCompetitionsAnnouncementAudience>('Participants')

  const announcementPending = ref(false)

  const announcementError = ref<string | null>(null)

  const RoleLabel: Record<CompetitionAdminRole, string> = {
    Owner: "ui.owner",
    Manager: "ui.administrator",
    Judge: "ui.judge",
    Observer: "ui.observer",
  }

  async function refresh() {
    const { data, error: e } = await adminGetCompetition({ path: { competitionId } })
    if (e || !data) {
      error.value = parseApiError(e, translate("ui.loadingCompetitionFailed")).message
      return
    }
    competition.value = data.competition ?? null
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

  async function publishAnnouncement() {
    if (announcementPending.value) return
    const title = announcementTitle.value.trim()
    const body = announcementBody.value.trim()
    announcementError.value = !title
      ? translate("ui.pleaseEnterANotificationTitle")
      : !body
        ? translate("ui.pleaseEnterNotificationContent")
        : null
    if (announcementError.value) return

    announcementPending.value = true
    try {
      const { error: requestError } = await adminCreateCompetitionAnnouncement({
        path: { competitionId },
        body: { title, body, audience: announcementAudience.value },
      })
      if (requestError) throw requestError
      toast.success(translate("ui.competitionNoticeHasBeenReleased"))
      announcementOpen.value = false
      announcementTitle.value = ''
      announcementBody.value = ''
      announcementError.value = null
    }
    catch (requestError) {
      announcementError.value = parseApiError(requestError, translate("ui.failedToPostContestNotification")).message
      toast.error(announcementError.value)
    }
    finally {
      announcementPending.value = false
    }
  }

  function setAnnouncementOpen(open: boolean) {
    if (announcementPending.value) return
    announcementOpen.value = open
    if (!open) announcementError.value = null
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
      label: translate("ui.operations"),
      items: [
        { to: base, label: translate("ui.overview"), icon: LayoutDashboard, exact: true },
        { to: `${base}/configuration`, label: translate("ui.configuration"), icon: Settings },
        { to: `${base}/tracks`, label: translate("ui.tracks"), icon: GitBranch },
        { to: `${base}/challenges`, label: translate("ui.challenge"), icon: Puzzle },
        { to: `${base}/teams`, label: translate("ui.teamManagement"), icon: Users },
      ],
    },
    {
      label: translate("ui.monitoring"),
      items: [
        ...(competition.value?.mode === 'Awdp'
          ? [{ to: `/competitions/${competitionId}/awdp-live`, label: translate("ui.controlScreen"), icon: Orbit }]
          : [{ to: `/competitions/${competitionId}/live`, label: translate("ui.3dLiveScreen"), icon: Orbit }]),
        { to: `/competitions/${competitionId}/events`, label: translate("ui.activity"), icon: Activity },
        { to: `${base}/submissions`, label: translate("ui.submissions"), icon: FileCheck },
        { to: `${base}/runtimes`, label: translate("ui.runtime"), icon: Container },
        { to: `${base}/traffic-captures`, label: translate("runtime.trafficCaptures"), icon: Network },
        { to: `${base}/cheats`, label: translate("ui.cheating"), icon: ShieldAlert },
        { to: `${base}/leaderboard`, label: translate("ui.leaderboard"), icon: Trophy },
      ],
    },
    {
      label: translate("ui.management"),
      items: [
        { to: `${base}/writeups`, label: translate('writeUp.review'), icon: ClipboardCheck },
        { to: `${base}/exports`, label: translate("ui.export"), icon: Download },
        { to: `${base}/webhooks`, label: translate("webhook.title"), icon: Webhook },
        ...(competition.value?.mode === 'Ctf'
          ? [{ to: `${base}/progression`, label: translate('progression.title'), icon: GitBranch }]
          : []),
        ...(canManagePermissions.value
          ? [{ to: `${base}/permissions`, label: translate("ui.permissions"), icon: KeyRound }]
          : []),
      ],
    },
  ])

  onMounted(async () => {
    loading.value = true
    await refresh()
    if (competition.value) await resolveRole()
    loading.value = false
  })

  const CompetitionStatusBadge = markRaw(CompetitionStatusBadgeComponent)

  const GameModeBadge = markRaw(GameModeBadgeComponent)

  const AppWorkspaceNav = markRaw(AppWorkspaceNavComponent)

  const viewBindings = {
      Megaphone,
      competition,
      role,
      loading,
      error,
      canAnnounce,
      announcementOpen,
      announcementTitle,
      announcementBody,
      announcementAudience,
      announcementPending,
      announcementError,
      RoleLabel,
      publishAnnouncement,
      setAnnouncementOpen,
      navGroups,
      activePath,
      isProgressionPage,
      isWriteUpReview,
      usesPageScroll,
      CompetitionStatusBadge,
      GameModeBadge,
      AppWorkspaceNav
    }
  const viewState = proxyRefs(viewBindings)

  function onClickAnnouncementOpen(value: typeof viewState.announcementOpen) {
    viewState.announcementOpen = value
  }

  function onInputAnnouncementError(value: typeof viewState.announcementError) {
    viewState.announcementError = value
  }

  return { ...viewBindings, onClickAnnouncementOpen, onInputAnnouncementError }
}

export type AdminCompetitionsByIdPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useAdminCompetitionsByIdPage>>>
