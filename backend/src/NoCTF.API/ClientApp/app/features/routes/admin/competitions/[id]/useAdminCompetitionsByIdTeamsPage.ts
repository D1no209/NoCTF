
import { api } from '../../../../../lib/api'

import { RequestPolicyOption } from '../../../../../lib/api'
import { message as describeMessage } from '../../../../../utils/i18n'
import type { UiMessage } from '../../../../../utils/i18n'
import { proxyRefs } from 'vue'
import { createLatestRequestGuard } from '~/lib/latest-request'
import { useAdminDetailRoute } from '~/features/admin/useAdminDetailRoute'
import { adminUserPath } from '~/features/admin/admin-navigation'
import { markRaw } from 'vue'

import { toast } from '../../../../../utils/message-toast'

import type { NoCTFAPIEndpointsAdministrationTeamsAdminTeamBanCaseResponse, NoCTFAPIEndpointsAuthenticationPublicUserProfileResponse, NoCTFAPIEndpointsChallengesChallengeSummaryResponse, NoCTFAPIEndpointsCompetitionsTracksCompetitionTrackResponse, NoCTFAPIEndpointsTeamsTeamResponse } from '../../../../../api/models'
import { useCompetitionAdmin } from '../../../../../lib/admin-competition'
import { competitionTrackErrorMessage } from '../../../../../lib/competition-track'
import PrivateAccountPanelComponent from '../../../../account/PrivateAccountPanel.vue'
import { useOffsetPagination } from '../../../../../composables/useOffsetPagination'

/** Owns state, effects and commands for AdminCompetitionsByIdTeamsPage. */
export function useAdminCompetitionsByIdTeamsPage() {
  const { competitionId, canJudge, canWrite } = useCompetitionAdmin()

  const route = useRoute()

  const teams = ref<NoCTFAPIEndpointsTeamsTeamResponse[]>([])

  const search = ref('')

  const loading = ref(true)

  const error = ref<UiMessage | null>(null)

  const pendingId = ref<string | null>(null)

  const tracks = ref<NoCTFAPIEndpointsCompetitionsTracksCompetitionTrackResponse[]>([])

  const tracksEnabled = ref(false)

  const registrationStatusOptions = [
    { value: 'Unregistered', label: "common.label.registered" },
    { value: 'Pending', label: "common.label.pendingApproval" },
    { value: 'Approved', label: "common.label.passed" },
    { value: 'Rejected', label: "common.label.rejected" },
  ] as const

  const selection = useAdminDetailRoute<NoCTFAPIEndpointsTeamsTeamResponse>('teamId', `/admin/competitions/${competitionId}/teams`, async (teamId, signal) => {
    let error: unknown;
    const data = await api.api.v1.admin.competitions.byCompetitionId(competitionId).teams.byTeamId(teamId).get({ options: [new RequestPolicyOption({ signal: signal })] }).catch(cause => { error = cause; return undefined });
    if (error || !data) throw error ?? new Error(translate('adminNavigation.notFound'))
    return data
  })
  const { data: selectedTeam, open: teamDetailOpen, loading: teamLoading, error: teamDetailError } = selection

  const teamMembers = ref<NoCTFAPIEndpointsAuthenticationPublicUserProfileResponse[]>([])

  const teamDetailLoading = ref(false)

  const teamInvitationToken = ref<string | null>(null)

  const teamInvitationLoading = ref(false)

  const teamInvitationError = ref<UiMessage | null>(null)

  const expandedMemberId = ref<string | null>(null)
  const memberRequests = createLatestRequestGuard()
  const invitationRequests = createLatestRequestGuard()
  onScopeDispose(() => { memberRequests.invalidate(); invitationRequests.invalidate() })

  function setExpandedMember(value: unknown): void {
    expandedMemberId.value = typeof value === 'string' ? value : null
  }

  async function loadTeamInvitation(
    team: NoCTFAPIEndpointsTeamsTeamResponse | null = selectedTeam.value,
  ): Promise<void> {
    const request = invitationRequests.begin()
    teamInvitationToken.value = null
    teamInvitationError.value = null
    if (!canWrite.value || !team?.id) return
    teamInvitationLoading.value = true
    try {
      let requestError: unknown;
      const data = await api.api.v1.admin.competitions.byCompetitionId(competitionId).teams.byTeamId(team.id).invitationToken.get().catch(cause => { requestError = cause; return undefined });
      if (!invitationRequests.isCurrent(request) || selectedTeam.value?.id !== team.id) return
      if (requestError || !data?.invitationToken) {
        teamInvitationError.value = parseApiError(
          requestError,
          describeMessage('competitions.competitionsBy.error.loadInvitationCodeFailed'),
        ).displayMessage
        return
      }
      teamInvitationToken.value = data.invitationToken
    }
    catch (requestError) {
      if (invitationRequests.isCurrent(request) && selectedTeam.value?.id === team.id) {
        teamInvitationError.value = parseApiError(
          requestError,
          describeMessage('competitions.competitionsBy.error.loadInvitationCodeFailed'),
        ).displayMessage
      }
    }
    finally {
      if (invitationRequests.isCurrent(request) && selectedTeam.value?.id === team.id) teamInvitationLoading.value = false
    }
  }

  async function copyTeamInvitation(): Promise<void> {
    if (!teamInvitationToken.value) return
    try {
      await navigator.clipboard.writeText(teamInvitationToken.value)
      toast.success(describeMessage('competitions.competitionsBy.label.invitationCodeCopied'))
    }
    catch {
      toast.error(describeMessage('common.kohPanel.error.copyManuallySelectFailed'))
    }
  }

  function reloadTeamInvitation(): void {
    void loadTeamInvitation()
  }

  const pagination = useOffsetPagination<NoCTFAPIEndpointsTeamsTeamResponse>(async ({ offset, limit, desc }) => {
    let requestError: unknown;
    const data = await api.api.v1.admin.competitions.byCompetitionId(competitionId).teams.get({ queryParameters: { keyword: search.value.trim() || undefined, offset, limit, desc } }).catch(cause => { requestError = cause; return undefined });
    if (requestError || !data) throw requestError ?? new Error('Failed to load teams.')
    teams.value = data.items ?? []
    return { items: teams.value, total: data.total ?? 0 }
  })

  const teamDisplayNames = computed(() => buildTeamDisplayNames(teams.value))

  const displayTeamName = (team: NoCTFAPIEndpointsTeamsTeamResponse) =>
    teamDisplayName(team, teamDisplayNames.value)

  const scoreAdjustmentTeam = ref<NoCTFAPIEndpointsTeamsTeamResponse | null>(null)

  const scoreAdjustmentChallenges = ref<NoCTFAPIEndpointsChallengesChallengeSummaryResponse[]>([])

  const scoreAdjustmentChallengeId = ref('')

  const scoreAdjustmentDelta = ref(0)

  const scoreAdjustmentLoading = ref(false)

  const scoreAdjustmentPending = ref(false)

  const scoreAdjustmentError = ref<UiMessage | null>(null)

  const scoreAdjustmentValid = computed(() =>
    Boolean(scoreAdjustmentTeam.value?.id)
    && Boolean(scoreAdjustmentChallengeId.value)
    && Number.isInteger(scoreAdjustmentDelta.value)
    && scoreAdjustmentDelta.value !== 0,
  )

  async function openScoreAdjustment(team: NoCTFAPIEndpointsTeamsTeamResponse): Promise<void> {
    if (!team.id || !canJudge.value) return
    scoreAdjustmentTeam.value = team
    scoreAdjustmentChallenges.value = []
    scoreAdjustmentChallengeId.value = ''
    scoreAdjustmentDelta.value = 0
    scoreAdjustmentError.value = null
    scoreAdjustmentLoading.value = true
    try {
      let requestError: unknown;
      const data = await api.api.v1.admin.competitions.byCompetitionId(competitionId).challenges.get({ queryParameters: { includeDeleted: false } }).catch(cause => { requestError = cause; return undefined });
      if (requestError) throw requestError
      if (scoreAdjustmentTeam.value?.id !== team.id) return
      scoreAdjustmentChallenges.value = (data?.items ?? [])
        .filter(challenge => Boolean(challenge.id) && !challenge.deletedAt)
        .sort((left, right) => (left.order ?? 0) - (right.order ?? 0))
    }
    catch (requestError) {
      if (scoreAdjustmentTeam.value?.id === team.id)
        scoreAdjustmentError.value = parseApiError(requestError, describeMessage("administration.competitionsBy.error.loadChallengeListFailed")).displayMessage
    }
    finally {
      if (scoreAdjustmentTeam.value?.id === team.id) scoreAdjustmentLoading.value = false
    }
  }

  function closeScoreAdjustment(open: boolean): void {
    if (!open && !scoreAdjustmentPending.value) scoreAdjustmentTeam.value = null
  }

  async function submitScoreAdjustment(): Promise<void> {
    const teamId = scoreAdjustmentTeam.value?.id
    if (!teamId || !scoreAdjustmentValid.value || scoreAdjustmentPending.value) return
    scoreAdjustmentPending.value = true
    scoreAdjustmentError.value = null
    try {
      let requestError: unknown;
      await api.api.v1.admin.competitions.byCompetitionId(competitionId).gameplayFacts.manualAdjustments.post({
          teamId,
          competitionChallengeId: scoreAdjustmentChallengeId.value,
          delta: scoreAdjustmentDelta.value,
        }).catch(cause => { requestError = cause; return undefined });
      if (requestError) throw requestError
      toast.success(describeMessage("administration.label.scoreAdjustmentRecorded"))
      scoreAdjustmentTeam.value = null
    }
    catch (requestError) {
      scoreAdjustmentError.value = parseApiError(requestError, describeMessage("administration.competitionsBy.error.recordScoreAdjustmentFailed")).displayMessage
    }
    finally {
      scoreAdjustmentPending.value = false
    }
  }

  async function loadTeamMembers(team: NoCTFAPIEndpointsTeamsTeamResponse): Promise<void> {
    const request = memberRequests.begin()
    expandedMemberId.value = null
    teamMembers.value = []
    teamDetailLoading.value = true
    void loadTeamInvitation(team)
    const memberIds = team.memberIds ?? []
    const responses = await Promise.allSettled(memberIds.map(userId => api.api.v1.users.byUserId(userId).get()))
    if (memberRequests.isCurrent(request) && selectedTeam.value?.id === team.id) {
      teamMembers.value = responses.flatMap(response => response.status === 'fulfilled' && response.value ? [response.value] : [])
      teamDetailLoading.value = false
    }
  }

  async function openTeamDetail(team: NoCTFAPIEndpointsTeamsTeamResponse): Promise<void> {
    if (team.id) await selection.select(team.id)
  }
  watch(selectedTeam, (team) => {
    memberRequests.invalidate()
    invitationRequests.invalidate()
    teamMembers.value = []
    expandedMemberId.value = null
    teamInvitationToken.value = null
    teamInvitationError.value = null
    teamInvitationLoading.value = false
    teamDetailLoading.value = false
    if (team) void loadTeamMembers(team)
  }, { flush: 'sync' })

  async function load() {
    loading.value = true
    error.value = null
    let trackResultError: unknown;
    const trackResult = await api.api.v1.admin.competitions.byCompetitionId(competitionId).get().catch(cause => { trackResultError = cause; return undefined });
    await pagination.loadPage(pagination.page.value)
    if (pagination.error.value) error.value = pagination.error.value.message
    if (!trackResultError && trackResult) {
      tracks.value = trackResult.tracks?.items ?? []
      tracksEnabled.value = trackResult.tracks?.enabled ?? false
    }
    loading.value = false
  }

  watch(search, () => {
    pagination.reset()
    void load()
  })

  async function assignTrack(team: NoCTFAPIEndpointsTeamsTeamResponse, trackKey: string) {
    if (!team.id || !team.registrationStatus || !canWrite.value || !tracksEnabled.value || team.trackKey === trackKey) return
    pendingId.value = team.id
    try {
      let requestError: unknown;
      await api.api.v1.competitions.byCompetitionId(competitionId).teams.byTeamId(team.id).patch({ administration: { trackKey, registrationStatus: team.registrationStatus } }).catch(cause => { requestError = cause; return undefined });
      if (requestError) throw requestError
      toast.success(describeMessage("administration.label.teamTrackUpdated"))
      await load()
    }
    catch (requestError) {
      toast.error(competitionTrackErrorMessage(requestError, translate("administration.competitionsBy.error.updateTeamTrackFailed")))
    }
    finally {
      pendingId.value = null
    }
  }

  function assignTrackValue(team: NoCTFAPIEndpointsTeamsTeamResponse, value: unknown): void {
    if (typeof value === 'string') void assignTrack(team, value)
  }

  async function setRegistrationStatus(
    team: NoCTFAPIEndpointsTeamsTeamResponse,
    registrationStatus: NonNullable<NoCTFAPIEndpointsTeamsTeamResponse['registrationStatus']>,
  ) {
    if (!team.id || !team.trackKey || team.registrationStatus === registrationStatus) return
    pendingId.value = team.id
    try {
      const path = { competitionId, teamId: team.id }

      await api.api.v1.competitions.byCompetitionId(path.competitionId).teams.byTeamId(path.teamId).patch({
          administration: {
            trackKey: team.trackKey,
            registrationStatus,
          },
        });
      toast.success(describeMessage("administration.label.completed"))
      await load()
    }
    catch (e) {
      toast.error(parseApiError(e).displayMessage)
    }
    finally {
      pendingId.value = null
    }
  }

  function setRegistrationStatusValue(
    team: NoCTFAPIEndpointsTeamsTeamResponse,
    value: unknown,
  ): void {
    if (registrationStatusOptions.some(option => option.value === value))
      void setRegistrationStatus(team, value as NonNullable<NoCTFAPIEndpointsTeamsTeamResponse['registrationStatus']>)
  }

  const banDialog = ref<{ team: NoCTFAPIEndpointsTeamsTeamResponse; mode: 'ban' | 'correct' } | null>(null)

  const banReason = ref('')

  const banAnnouncePublicly = ref(false)

  const banPending = ref(false)

  const banReasonValid = computed(() => {
    const length = banReason.value.trim().length
    return length <= 512 && (banDialog.value?.mode === 'correct' ? length >= 8 : length > 0)
  })

  function openBan(team: NoCTFAPIEndpointsTeamsTeamResponse, mode: 'ban' | 'correct') {
    banDialog.value = { team, mode }
    banReason.value = ''
    banAnnouncePublicly.value = false
  }

  async function submitBan() {
    const ctx = banDialog.value
    if (!ctx?.team.id || !banReasonValid.value) return
    banPending.value = true
    try {
      const path = { competitionId, teamId: ctx.team.id }
      await (ctx.mode === 'ban'
        ? api.api.v1.competitions.byCompetitionId(path.competitionId).teams.byTeamId(path.teamId).patch({
              ban: {
                isBanned: true,
                reason: banReason.value.trim(),
                announcePublicly: banAnnouncePublicly.value,
              },
            })
        : api.api.v1.admin.competitions.byCompetitionId(path.competitionId).teams.byTeamId(path.teamId).correctBan.post({ reason: banReason.value.trim() }));
      toast.success(ctx.mode === 'ban' ? translate("administration.label.teamBanned") : translate("administration.competitionsBy.label.banCorrected"))
      banDialog.value = null
      await load()
    }
    catch (e) {
      toast.error(parseApiError(e).displayMessage)
    }
    finally {
      banPending.value = false
    }
  }

  const appeals = ref<NoCTFAPIEndpointsAdministrationTeamsAdminTeamBanCaseResponse[]>([])

  const appealsLoading = ref(true)

  const appealsError = ref<UiMessage | null>(null)

  const appealDialog = ref<{ banCase: NoCTFAPIEndpointsAdministrationTeamsAdminTeamBanCaseResponse; mode: 'accept' | 'uphold' } | null>(null)

  const appealReason = ref('')

  const appealPending = ref(false)

  async function loadAppeals() {
    appealsLoading.value = true
    let error: unknown;
    const data = await api.api.v1.admin.competitions.byCompetitionId(competitionId).teamBanAppeals.get().catch(cause => { error = cause; return undefined });
    if (error || !data) {
      appealsError.value = parseApiError(error).displayMessage
    }
    else {
      appealsError.value = null
      appeals.value = data.items ?? []
      await nextTick()
      const appealId = typeof route.query.appeal === 'string' ? route.query.appeal : null
      if (appealId) document.getElementById(`appeal-${appealId}`)?.scrollIntoView({ block: 'center' })
    }
    appealsLoading.value = false
  }

  function openAppeal(banCase: NoCTFAPIEndpointsAdministrationTeamsAdminTeamBanCaseResponse, mode: 'accept' | 'uphold') {
    appealDialog.value = { banCase, mode }
    appealReason.value = ''
  }

  async function submitAppeal() {
    const ctx = appealDialog.value
    const appealId = ctx?.banCase.appeal?.id
    if (!ctx || !appealId) return
    if (!appealReason.value.trim()) return
    appealPending.value = true
    try {
      const path = { competitionId, appealId }

      await api.api.v1.admin.competitions.byCompetitionId(path.competitionId).teamBanAppeals.byAppealId(path.appealId).resolution.put({
          resolution: ctx.mode === 'accept' ? 'Accepted' : 'Upheld',
          reason: appealReason.value.trim(),
        });
      toast.success(ctx.mode === 'accept' ? translate("administration.label.appealAcceptedTeamUnblocked") : translate("administration.competitionsBy.description.appealDismissedBanMaintained"))
      appealDialog.value = null
      await Promise.all([load(), loadAppeals()])
    }
    catch (e) {
      toast.error(parseApiError(e).displayMessage)
    }
    finally {
      appealPending.value = false
    }
  }

  onMounted(() => {
    void load()
    void loadAppeals()
  })

  const PrivateAccountPanel = markRaw(PrivateAccountPanelComponent)

  const viewBindings = {
      adminUserPath, teamDetailOpen, teamLoading, teamDetailError,
      competitionId,
      canJudge,
      canWrite,
      teams,
      search,
      page: pagination.page,
      pageCount: pagination.pageCount,
      total: pagination.total,
      pageLimit: pagination.limit,
      pageLoading: pagination.loading,
      loadPage: pagination.loadPage,
      setPageSize: pagination.setPageSize,
      loading,
      error,
      pendingId,
      tracks,
      tracksEnabled,
      registrationStatusOptions,
      selectedTeam,
      teamMembers,
      teamDetailLoading,
      teamInvitationToken,
      teamInvitationLoading,
      teamInvitationError,
      loadTeamInvitation,
      reloadTeamInvitation,
      copyTeamInvitation,
      expandedMemberId,
      setExpandedMember,
      teamDisplayNames,
      displayTeamName,
      scoreAdjustmentTeam,
      scoreAdjustmentChallenges,
      scoreAdjustmentChallengeId,
      scoreAdjustmentDelta,
      scoreAdjustmentLoading,
      scoreAdjustmentPending,
      scoreAdjustmentError,
      scoreAdjustmentValid,
      openScoreAdjustment,
      closeScoreAdjustment,
      submitScoreAdjustment,
      openTeamDetail,
      assignTrack,
      assignTrackValue,
      setRegistrationStatusValue,
      banDialog,
      banReason,
      banAnnouncePublicly,
      banPending,
      banReasonValid,
      openBan,
      submitBan,
      appeals,
      appealsLoading,
      appealsError,
      appealDialog,
      appealReason,
      appealPending,
      openAppeal,
      submitAppeal,
      PrivateAccountPanel
    }
  const viewState = proxyRefs(viewBindings)

  function onUpdateOpenOpen(open: boolean) {
     selection.close(open)
     if (!open) {
       viewState.selectedTeam = null
       viewState.teamInvitationToken = null
       viewState.teamInvitationError = null
       viewState.teamInvitationLoading = false
       viewState.expandedMemberId = null
     }
  }

  function onClickScoreAdjustmentTeam(value: typeof viewState.scoreAdjustmentTeam) {
    viewState.scoreAdjustmentTeam = value
  }

  function onUpdateOpenBanDialog(v: boolean) {
     if (!v) viewState.banDialog = null
  }

  function onClickBanDialog(value: typeof viewState.banDialog) {
    viewState.banDialog = value
  }

  function onUpdateOpenAppealDialog(v: boolean) {
     if (!v) viewState.appealDialog = null
  }

  function onClickAppealDialog(value: typeof viewState.appealDialog) {
    viewState.appealDialog = value
  }

  return { ...viewBindings, onUpdateOpenOpen, onClickScoreAdjustmentTeam, onUpdateOpenBanDialog, onClickBanDialog, onUpdateOpenAppealDialog, onClickAppealDialog }
}

export type AdminCompetitionsByIdTeamsPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useAdminCompetitionsByIdTeamsPage>>>
