import { message as describeMessage } from '../../../../../utils/i18n'
import type { UiMessage } from '../../../../../utils/i18n'
import { proxyRefs } from 'vue'
import { markRaw } from 'vue'

import { toast } from '../../../../../utils/message-toast'
import { Copy, RefreshCw } from '@lucide/vue'
import { deleteTeamEndpoint, getMyTeamBanCase, getMyTeamEndpoint, getTeamInvitationEndpoint, leaveTeamEndpoint, listCompetitionTracks, patchCompetitionTeam, rotateTeamInvitationEndpoint, submitTeamBanAppeal, teamAvatarClear, teamAvatarReplace } from '../../../../../api'
import type { NoCtfapiEndpointsCompetitionsTracksCompetitionTrackResponse, NoCtfapiEndpointsTeamsMyTeamBanCaseResponse, NoCtfapiEndpointsTeamsTeamResponse } from '../../../../../api'
import { maximumAppealStatementLength, minimumAppealStatementLength, validateAppealStatement } from '../../../../../lib/participant-form-validation'
import { teamRegistrationErrorMessage } from '../../../../../lib/competition-track'
import { exceedsUploadLimit } from '../../../../account/upload-limits'
import CompetitionParticipantWorkspaceComponent from '../../../../competition/CompetitionParticipantWorkspace.vue'
import TeamMembersComponent from '../../../../teams/TeamMembers.vue'
import TeamRuntimeManagerComponent from '../../../../teams/TeamRuntimeManager.vue'

/** Owns state, effects and commands for CompetitionsByIdMyTeamPage. */
export function useCompetitionsByIdMyTeamPage() {
  const route = useRoute()

  const competitionId = route.params.id as string

  const { user } = useAuth()

  const competitionContext = inject(competitionContextKey)!

  const competition = computed(() => competitionContext.competition.value)

  const tracksEnabled = computed(() => competition.value?.tracksEnabled === true)

  const canEditOrganization = computed(() => competition.value?.status === 'Visible'
    || competition.value?.status === 'Published'
    || competition.value?.status === 'Running'
      && competition.value.allowTeamRegistrationWhileRunning === true)

  const selectableTracks = ref<NoCtfapiEndpointsCompetitionsTracksCompetitionTrackResponse[]>([])

  const tracksLoading = ref(false)

  const tracksError = ref<UiMessage | null>(null)

  async function loadTracks(): Promise<void> {
    selectableTracks.value = []
    tracksError.value = null
    if (!tracksEnabled.value) return
    tracksLoading.value = true
    try {
      const { data, error } = await listCompetitionTracks({ path: { competitionId } })
      if (error || !data) throw error
      selectableTracks.value = (data.items ?? []).filter(track => track.isPublicSelectable)
    }
    catch (error) {
      tracksError.value = parseApiError(
        error,
        describeMessage('competitions.competitionOverview.error.loadCompetitionTracksFailed'),
      ).displayMessage
    }
    finally {
      tracksLoading.value = false
    }
  }

  const team = ref<NoCtfapiEndpointsTeamsTeamResponse | null>(null)

  const loading = ref(true)

  const loadError = ref<UiMessage | null>(null)

  const invitationToken = ref<string | null>(null)

  const invitationLoading = ref(false)

  const invitationError = ref<UiMessage | null>(null)

  async function loadInvitationToken() {
    if (!team.value || team.value.captainId !== user.value?.userId || team.value.isBanned) {
      invitationToken.value = null
      invitationError.value = null
      return
    }
    invitationLoading.value = true
    invitationError.value = null
    const { data, error } = await getTeamInvitationEndpoint({
      path: { competitionId, teamId: team.value.id! },
    })
    invitationLoading.value = false
    if (error || !data?.invitationToken) {
      invitationError.value = parseApiError(error, describeMessage("competitions.competitionsBy.error.loadInvitationCodeFailed")).displayMessage
      return
    }
    invitationToken.value = data.invitationToken
  }

  async function load() {
    loadError.value = null
    const { data, error, response } = await getMyTeamEndpoint({ path: { competitionId } })
    loading.value = false
    if (error || !data) {
      team.value = null
      if (error && response?.status !== 404) loadError.value = parseApiError(error).displayMessage
      return
    }
    team.value = data
    await loadInvitationToken()
  }

  onMounted(() => {
    void load()
    void loadTracks()
  })

  watch(tracksEnabled, () => { void loadTracks() })

  let unwatchCompetition: (() => void) | undefined
  onMounted(() => {
    unwatchCompetition = watchCompetition(competitionId, {
      competitionEventChanged: (event) => {
        if (event.kind === 'TeamRegistrationChanged'
          || event.kind === 'TeamTrackChanged'
          || event.kind === 'TeamBanned'
          || event.kind === 'TeamUnbanned'
          || event.kind === 'TeamBanCorrectionPublished') {
          void load()
        }
        if (event.kind === 'TrackConfigurationUpdated'
          || event.kind === 'TrackRegistrationPolicyUpdated') {
          void loadTracks()
        }
      },
      onReconnected: () => { void Promise.all([load(), loadTracks()]) },
    })
  })
  onBeforeUnmount(() => unwatchCompetition?.())

  const isCaptain = computed(
    () => !!team.value && !!user.value && team.value.captainId === user.value.userId,
  )

  const rotating = ref(false)

  async function rotate() {
    if (!team.value) return
    rotating.value = true
    invitationError.value = null
    const { data, error } = await rotateTeamInvitationEndpoint({
      path: { competitionId, teamId: team.value.id! },
    })
    rotating.value = false
    if (error || !data?.invitationToken) {
      toast.error(parseApiError(error, describeMessage("competitions.competitionsBy.error.rotateInvitationCodeFailed")).displayMessage)
      return
    }
    invitationToken.value = data.invitationToken
    toast.success(describeMessage("competitions.competitionsBy.description.invitationCodeRotatedOld"))
  }

  async function copyToken() {
    if (!invitationToken.value) return
    try {
      await navigator.clipboard.writeText(invitationToken.value)
      toast.success(describeMessage("competitions.competitionsBy.label.invitationCodeCopied"))
    }
    catch {
      toast.error(describeMessage("common.kohPanel.error.copyManuallySelectFailed"))
    }
  }

  const renameOpen = ref(false)

  const renameValue = ref('')

  const renameTrackKey = ref('')

  const selectedRenameTrack = computed(() => selectableTracks.value.find(
    track => track.key === renameTrackKey.value,
  ))

  const renameTrackChanged = computed(() => Boolean(
    tracksEnabled.value
    && renameTrackKey.value
    && renameTrackKey.value !== team.value?.trackKey,
  ))

  const renameValid = computed(() => Boolean(
    team.value
    && canEditOrganization.value
    && renameValue.value.trim()
    && (!tracksEnabled.value || renameTrackKey.value),
  ))

  const renamePending = ref(false)

  function openRename() {
    renameValue.value = team.value?.name ?? ''
    renameTrackKey.value = team.value?.trackKey ?? ''
    renameOpen.value = true
  }

  function showOrganizationChangeSuccess(updatedTeam: NoCtfapiEndpointsTeamsTeamResponse): void {
    toast.success(updatedTeam.registrationStatus === 'Unregistered'
      ? translate('competitions.competitionsBy.label.teamDraftSavedSubmit')
      : translate('competitions.competitionsBy.label.teamChangesSavedApproved'))
  }

  async function submitRename() {
    if (!team.value || !renameValid.value) return
    renamePending.value = true
    const { data, error } = await patchCompetitionTeam({
      path: { competitionId, teamId: team.value.id! },
      body: { profile: {
        name: renameValue.value.trim(),
        trackKey: tracksEnabled.value ? renameTrackKey.value : null,
        trackInvitationCode: null,
      } },
    })
    renamePending.value = false
    if (error || !data) {
      toast.error(teamRegistrationErrorMessage(error, translate("competitions.competitionsBy.error.modifyTeamNameFailed")))
      return
    }
    team.value = data
    renameOpen.value = false
    showOrganizationChangeSuccess(data)
  }

  const { configuration: platformConfiguration } = usePlatform()

  const maximumAvatarBytes = computed(() =>
    platformConfiguration.value?.imageUploadLimits?.maximumAvatarBytes ?? null)

  const avatarInputKey = ref(0)

  const avatarPending = ref(false)

  async function replaceTeamAvatar(event: Event): Promise<void> {
    const file = (event.target as HTMLInputElement).files?.[0] ?? null
    avatarInputKey.value += 1
    if (!team.value || !file || avatarPending.value || !canEditOrganization.value) return
    if (!['image/jpeg', 'image/png', 'image/webp'].includes(file.type)) {
      toast.error(describeMessage('common.competitionsBy.validation.avatarJpegFormat'))
      return
    }
    if (exceedsUploadLimit(file.size, maximumAvatarBytes.value)) {
      toast.error(describeMessage('common.error.uploadTooLarge'))
      return
    }
    avatarPending.value = true
    try {
      const { error } = await teamAvatarReplace({
        path: { competitionId, teamId: team.value.id! },
        body: { file },
      })
      if (error) throw error
      await load()
      if (team.value) showOrganizationChangeSuccess(team.value)
    }
    catch (error) {
      toast.error(teamRegistrationErrorMessage(error, translate('competitions.competitionsBy.error.updateTeamAvatarFailed')))
    }
    finally {
      avatarPending.value = false
    }
  }

  async function clearTeamAvatar(): Promise<void> {
    if (!team.value?.avatarUrl || avatarPending.value || !canEditOrganization.value) return
    avatarPending.value = true
    try {
      const { error } = await teamAvatarClear({
        path: { competitionId, teamId: team.value.id! },
      })
      if (error) throw error
      await load()
      if (team.value) showOrganizationChangeSuccess(team.value)
    }
    catch (error) {
      toast.error(teamRegistrationErrorMessage(error, translate('competitions.competitionsBy.error.clearTeamAvatarFailed')))
    }
    finally {
      avatarPending.value = false
    }
  }

  const transferOpen = ref(false)

  const transferTarget = ref('')

  const transferPending = ref(false)

  const transferableMembers = computed(() =>
    (team.value?.memberIds ?? []).filter((id) => id !== team.value?.captainId),
  )

  async function submitTransfer() {
    if (!team.value || !transferTarget.value) return
    transferPending.value = true
    const { data, error } = await patchCompetitionTeam({
      path: { competitionId, teamId: team.value.id! },
      body: {
        membership: {
          captainId: transferTarget.value,
          memberIds: team.value.memberIds ?? [],
        },
      },
    })
    transferPending.value = false
    if (error) {
      toast.error(parseApiError(error, describeMessage("competitions.error.transferCaptainFailed")).displayMessage)
      return
    }
    transferOpen.value = false
    if (data) team.value = data
    toast.success(data?.registrationStatus === 'Unregistered'
      ? translate('competitions.validation.captainTransferredRequired')
      : translate("competitions.label.captainTransferred"))
    if (!data) await load()
  }

  const acting = ref(false)

  async function disband() {
    if (!team.value) return
    acting.value = true
    const { error } = await deleteTeamEndpoint({
      path: { competitionId, teamId: team.value.id! },
    })
    acting.value = false
    if (error) {
      toast.error(parseApiError(error, describeMessage("competitions.competitionsBy.error.disbandTeamFailed")).displayMessage)
      return
    }
    toast.success(describeMessage("competitions.competitionsBy.label.teamDisbanded"))
    team.value = null
  }

  async function leave() {
    acting.value = true
    const { error } = await leaveTeamEndpoint({ path: { competitionId } })
    acting.value = false
    if (error) {
      toast.error(parseApiError(error, describeMessage("competitions.competitionsBy.error.quitTeamFailed")).displayMessage)
      return
    }
    toast.success(describeMessage("competitions.label.leftTeam"))
    team.value = null
  }

  const banCase = ref<NoCtfapiEndpointsTeamsMyTeamBanCaseResponse | null>(null)

  const appealOpen = ref(false)

  const appealStatement = ref('')

  const appealPending = ref(false)

  const appealError = ref<UiMessage | null>(null)

  const banCaseError = ref<UiMessage | null>(null)

  async function loadBanCase() {
    banCaseError.value = null
    const { data, error } = await getMyTeamBanCase({ path: { competitionId } })
    if (error || !data) {
      banCaseError.value = parseApiError(error, describeMessage("competitions.competitionsBy.error.loadBanAppealFailed")).displayMessage
      return
    }
    banCase.value = data
  }

  watch(
    () => team.value?.isBanned,
    (banned) => {
      if (banned) void loadBanCase()
    },
  )

  const appealStatusLabel = (status?: string) =>
    ({ Submitted: translate("competitions.label.appealing"), Upheld: translate("common.label.dismissed"), Accepted: translate("common.label.passed") } as Record<string, string>)[String(status)] ?? translate("common.label.unknown")

  async function submitAppeal() {
    if (appealPending.value) return
    appealError.value = validateAppealStatement(appealStatement.value)
    if (appealError.value) return

    appealPending.value = true
    try {
      const { error } = await submitTeamBanAppeal({
        path: { competitionId },
        body: { statement: appealStatement.value.trim() },
      })
      if (error) {
        appealError.value = parseApiError(error, describeMessage("competitions.error.submitAppealFailed")).displayMessage
        toast.error(appealError.value)
        return
      }
      toast.success(describeMessage("competitions.label.appealSubmitted"))
      appealOpen.value = false
      appealStatement.value = ''
      appealError.value = null
      await loadBanCase()
    }
    catch (error) {
      appealError.value = parseApiError(error, describeMessage("competitions.error.submitAppealFailed")).displayMessage
      toast.error(appealError.value)
    }
    finally {
      appealPending.value = false
    }
  }

  function setAppealOpen(open: boolean) {
    if (appealPending.value) return
    appealOpen.value = open
    if (!open) appealError.value = null
  }

  const CompetitionParticipantWorkspace = markRaw(CompetitionParticipantWorkspaceComponent)

  const TeamMembers = markRaw(TeamMembersComponent)

  const TeamRuntimeManager = markRaw(TeamRuntimeManagerComponent)

  const viewBindings = {
      Copy,
      RefreshCw,
      maximumAppealStatementLength,
      minimumAppealStatementLength,
      competitionId,
      competition,
      tracksEnabled,
      canEditOrganization,
      selectableTracks,
      tracksLoading,
      tracksError,
      loadTracks,
      team,
      loading,
      loadError,
      load,
      isCaptain,
      invitationToken,
      invitationLoading,
      invitationError,
      loadInvitationToken,
      rotating,
      rotate,
      copyToken,
      renameOpen,
      renameValue,
      renameTrackKey,
      selectedRenameTrack,
      renameTrackChanged,
      renameValid,
      renamePending,
      openRename,
      submitRename,
      maximumAvatarBytes,
      avatarInputKey,
      avatarPending,
      replaceTeamAvatar,
      clearTeamAvatar,
      transferOpen,
      transferTarget,
      transferPending,
      transferableMembers,
      submitTransfer,
      acting,
      disband,
      leave,
      banCase,
      appealOpen,
      appealStatement,
      appealPending,
      appealError,
      banCaseError,
      loadBanCase,
      appealStatusLabel,
      submitAppeal,
      setAppealOpen,
      CompetitionParticipantWorkspace,
      TeamMembers,
      TeamRuntimeManager
    }
  const viewState = proxyRefs(viewBindings)

  function onInputAppealError(value: typeof viewState.appealError) {
    viewState.appealError = value
  }

  return { ...viewBindings, onInputAppealError }
}

export type CompetitionsByIdMyTeamPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useCompetitionsByIdMyTeamPage>>>
