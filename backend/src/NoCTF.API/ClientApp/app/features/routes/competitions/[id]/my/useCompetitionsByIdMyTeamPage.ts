import { proxyRefs } from 'vue'
import { markRaw } from 'vue'

import { toast } from 'vue-sonner'
import { Copy, RefreshCw } from '@lucide/vue'
import { deleteTeamEndpoint, getMyTeamBanCase, getMyTeamEndpoint, leaveTeamEndpoint, resubmitTeamRegistrationEndpoint, rotateTeamInvitationEndpoint, submitTeamBanAppeal, transferTeamCaptainEndpoint, updateTeamEndpoint } from '../../../../../api'
import type { NoCtfapiEndpointsTeamsMyTeamBanCaseResponse, NoCtfapiEndpointsTeamsTeamResponse } from '../../../../../api'
import { maximumAppealStatementLength, minimumAppealStatementLength, validateAppealStatement } from '../../../../../lib/participant-form-validation'
import CompetitionParticipantWorkspaceComponent from '../../../../competition/CompetitionParticipantWorkspace.vue'
import TeamMembersComponent from '../../../../teams/TeamMembers.vue'

/** Owns state, effects and commands for CompetitionsByIdMyTeamPage. */
export function useCompetitionsByIdMyTeamPage() {
  const route = useRoute()

  const competitionId = route.params.id as string

  const { user } = useAuth()

  const team = ref<NoCtfapiEndpointsTeamsTeamResponse | null>(null)

  const loading = ref(true)

  const loadError = ref<string | null>(null)

  async function load() {
    const { data, error } = await getMyTeamEndpoint({ path: { competitionId } })
    loading.value = false
    if (error || !data) {
      team.value = null
      return
    }
    team.value = data
  }

  onMounted(load)

  const isCaptain = computed(
    () => !!team.value && !!user.value && team.value.captainId === user.value.userId,
  )

  const invitationToken = ref<string | null>(null)

  const rotating = ref(false)

  async function rotate() {
    if (!team.value) return
    rotating.value = true
    const { data, error } = await rotateTeamInvitationEndpoint({
      path: { competitionId, teamId: team.value.id! },
    })
    rotating.value = false
    if (error || !data?.invitationToken) {
      toast.error(parseApiError(error, translate("ui.failedToRotateInvitationCode")).message)
      return
    }
    invitationToken.value = data.invitationToken
    toast.success(translate("ui.theInvitationCodeHasBeenRotatedAndTheOldInvitation"))
  }

  async function copyToken() {
    if (!invitationToken.value) return
    try {
      await navigator.clipboard.writeText(invitationToken.value)
      toast.success(translate("ui.invitationCodeHasBeenCopied"))
    }
    catch {
      toast.error(translate("ui.copyFailedPleaseManuallySelectCopy"))
    }
  }

  const renameOpen = ref(false)

  const renameValue = ref('')

  const renamePending = ref(false)

  function openRename() {
    renameValue.value = team.value?.name ?? ''
    renameOpen.value = true
  }

  async function submitRename() {
    if (!team.value || !renameValue.value.trim()) return
    renamePending.value = true
    const { data, error } = await updateTeamEndpoint({
      path: { competitionId, teamId: team.value.id! },
      body: { name: renameValue.value.trim() },
    })
    renamePending.value = false
    if (error || !data) {
      toast.error(parseApiError(error, translate("ui.failedToModifyTeamName")).message)
      return
    }
    team.value = data
    renameOpen.value = false
    toast.success(translate("ui.teamNameHasBeenUpdated"))
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
    const { error } = await transferTeamCaptainEndpoint({
      path: { competitionId, teamId: team.value.id! },
      body: { newCaptainId: transferTarget.value },
    })
    transferPending.value = false
    if (error) {
      toast.error(parseApiError(error, translate("ui.transferOfCaptainFailed")).message)
      return
    }
    transferOpen.value = false
    toast.success(translate("ui.captainHasBeenTransferred"))
    await load()
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
      toast.error(parseApiError(error, translate("ui.failedToDisbandTheTeam")).message)
      return
    }
    toast.success(translate("ui.theTeamHasBeenDisbanded"))
    team.value = null
  }

  async function leave() {
    acting.value = true
    const { error } = await leaveTeamEndpoint({ path: { competitionId } })
    acting.value = false
    if (error) {
      toast.error(parseApiError(error, translate("ui.failedToQuitTheTeam")).message)
      return
    }
    toast.success(translate("ui.hasLeftTheTeam"))
    team.value = null
  }

  async function resubmit() {
    if (!team.value) return
    acting.value = true
    const { error } = await resubmitTeamRegistrationEndpoint({
      path: { competitionId, teamId: team.value.id! },
    })
    acting.value = false
    if (error) {
      toast.error(parseApiError(error, translate("ui.failedToResubmitRegistration")).message)
      return
    }
    toast.success(translate("ui.registrationHasBeenResubmittedAndIsAwaitingReview"))
    await load()
  }

  const banCase = ref<NoCtfapiEndpointsTeamsMyTeamBanCaseResponse | null>(null)

  const appealOpen = ref(false)

  const appealStatement = ref('')

  const appealPending = ref(false)

  const appealError = ref<string | null>(null)

  const banCaseError = ref<string | null>(null)

  async function loadBanCase() {
    banCaseError.value = null
    const { data, error } = await getMyTeamBanCase({ path: { competitionId } })
    if (error || !data) {
      banCaseError.value = parseApiError(error, translate("ui.failedToLoadTheBanAndAppealStatus")).message
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
    ({ Submitted: translate("ui.appealing"), Upheld: translate("ui.dismissed"), Accepted: translate("ui.passed") } as Record<string, string>)[String(status)] ?? translate("ui.unknown")

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
        appealError.value = parseApiError(error, translate("ui.failedToSubmitAppeal")).message
        toast.error(appealError.value)
        return
      }
      toast.success(translate("ui.appealSubmitted"))
      appealOpen.value = false
      appealStatement.value = ''
      appealError.value = null
      await loadBanCase()
    }
    catch (error) {
      appealError.value = parseApiError(error, translate("ui.failedToSubmitAppeal")).message
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

  const viewBindings = {
      Copy,
      RefreshCw,
      maximumAppealStatementLength,
      minimumAppealStatementLength,
      competitionId,
      team,
      loading,
      loadError,
      load,
      isCaptain,
      invitationToken,
      rotating,
      rotate,
      copyToken,
      renameOpen,
      renameValue,
      renamePending,
      openRename,
      submitRename,
      transferOpen,
      transferTarget,
      transferPending,
      transferableMembers,
      submitTransfer,
      acting,
      disband,
      leave,
      resubmit,
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
      TeamMembers
    }
  const viewState = proxyRefs(viewBindings)

  function onInputAppealError(value: typeof viewState.appealError) {
    viewState.appealError = value
  }

  return { ...viewBindings, onInputAppealError }
}

export type CompetitionsByIdMyTeamPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useCompetitionsByIdMyTeamPage>>>
