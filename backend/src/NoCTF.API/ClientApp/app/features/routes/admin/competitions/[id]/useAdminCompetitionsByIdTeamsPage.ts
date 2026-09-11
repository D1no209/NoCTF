import { proxyRefs } from 'vue'
import { markRaw } from 'vue'

import { toast } from 'vue-sonner'
import { adminCreateManualAdjustment, adminCorrectTeamBan, adminGetCompetition, adminListCompetitionChallenges, adminListTeamBanAppeals, adminListTeams, adminResolveTeamBanAppeal, patchCompetitionTeam, userProfileGet } from '../../../../../api'
import type { NoCtfapiEndpointsAdministrationTeamsAdminTeamBanCaseResponse, NoCtfapiEndpointsAuthenticationPublicUserProfileResponse, NoCtfapiEndpointsChallengesChallengeResponse, NoCtfapiEndpointsCompetitionsTracksCompetitionTrackResponse, NoCtfapiEndpointsTeamsTeamResponse } from '../../../../../api'
import { useCompetitionAdmin } from '../../../../../lib/admin-competition'
import { competitionTrackErrorMessage } from '../../../../../lib/competition-track'
import PrivateAccountPanelComponent from '../../../../account/PrivateAccountPanel.vue'

/** Owns state, effects and commands for AdminCompetitionsByIdTeamsPage. */
export function useAdminCompetitionsByIdTeamsPage() {
  const { competitionId, canJudge, canWrite } = useCompetitionAdmin()

  const route = useRoute()

  const teams = ref<NoCtfapiEndpointsTeamsTeamResponse[]>([])

  const loading = ref(true)

  const error = ref<string | null>(null)

  const pendingId = ref<string | null>(null)

  const tracks = ref<NoCtfapiEndpointsCompetitionsTracksCompetitionTrackResponse[]>([])

  const selectedTeam = ref<NoCtfapiEndpointsTeamsTeamResponse | null>(null)

  const teamMembers = ref<NoCtfapiEndpointsAuthenticationPublicUserProfileResponse[]>([])

  const teamDetailLoading = ref(false)

  const teamDisplayNames = computed(() => buildTeamDisplayNames(teams.value))

  const displayTeamName = (team: NoCtfapiEndpointsTeamsTeamResponse) =>
    teamDisplayName(team, teamDisplayNames.value)

  const scoreAdjustmentTeam = ref<NoCtfapiEndpointsTeamsTeamResponse | null>(null)

  const scoreAdjustmentChallenges = ref<NoCtfapiEndpointsChallengesChallengeResponse[]>([])

  const scoreAdjustmentChallengeId = ref('')

  const scoreAdjustmentDelta = ref(0)

  const scoreAdjustmentLoading = ref(false)

  const scoreAdjustmentPending = ref(false)

  const scoreAdjustmentError = ref<string | null>(null)

  const scoreAdjustmentValid = computed(() =>
    Boolean(scoreAdjustmentTeam.value?.id)
    && Boolean(scoreAdjustmentChallengeId.value)
    && Number.isInteger(scoreAdjustmentDelta.value)
    && scoreAdjustmentDelta.value !== 0,
  )

  async function openScoreAdjustment(team: NoCtfapiEndpointsTeamsTeamResponse): Promise<void> {
    if (!team.id || !canJudge.value) return
    scoreAdjustmentTeam.value = team
    scoreAdjustmentChallenges.value = []
    scoreAdjustmentChallengeId.value = ''
    scoreAdjustmentDelta.value = 0
    scoreAdjustmentError.value = null
    scoreAdjustmentLoading.value = true
    try {
      const { data, error: requestError } = await adminListCompetitionChallenges({
        path: { competitionId },
        query: { includeDeleted: false },
      })
      if (requestError) throw requestError
      if (scoreAdjustmentTeam.value?.id !== team.id) return
      scoreAdjustmentChallenges.value = (data?.items ?? [])
        .filter(challenge => Boolean(challenge.id) && !challenge.deletedAt)
        .sort((left, right) => (left.order ?? 0) - (right.order ?? 0))
    }
    catch (requestError) {
      if (scoreAdjustmentTeam.value?.id === team.id)
        scoreAdjustmentError.value = parseApiError(requestError, translate("ui.failedToLoadTheChallengeList")).message
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
      const { error: requestError } = await adminCreateManualAdjustment({
        path: { competitionId },
        body: {
          teamId,
          competitionChallengeId: scoreAdjustmentChallengeId.value,
          delta: scoreAdjustmentDelta.value,
        },
      })
      if (requestError) throw requestError
      toast.success(translate("ui.scoreAdjustmentRecorded"))
      scoreAdjustmentTeam.value = null
    }
    catch (requestError) {
      scoreAdjustmentError.value = parseApiError(requestError, translate("ui.failedToRecordTheScoreAdjustment")).message
    }
    finally {
      scoreAdjustmentPending.value = false
    }
  }

  async function openTeamDetail(team: NoCtfapiEndpointsTeamsTeamResponse): Promise<void> {
    selectedTeam.value = team
    teamMembers.value = []
    teamDetailLoading.value = true
    const memberIds = team.memberIds ?? []
    const responses = await Promise.all(memberIds.map(userId => userProfileGet({ path: { userId } })))
    if (selectedTeam.value?.id === team.id) {
      teamMembers.value = responses.flatMap(response => response.data ? [response.data] : [])
      teamDetailLoading.value = false
    }
  }

  async function load() {
    loading.value = true
    error.value = null
    const [teamResult, trackResult] = await Promise.all([
      adminListTeams({ path: { competitionId } }),
      adminGetCompetition({ path: { competitionId } }),
    ])
    if (teamResult.error || !teamResult.data) error.value = parseApiError(teamResult.error).message
    else teams.value = teamResult.data.items ?? []
    if (!trackResult.error && trackResult.data) {
      tracks.value = trackResult.data.tracks?.items ?? []
    }
    loading.value = false
  }

  async function assignTrack(team: NoCtfapiEndpointsTeamsTeamResponse, trackKey: string) {
    if (!team.id || !team.registrationStatus || !canWrite.value || team.trackKey === trackKey) return
    pendingId.value = team.id
    try {
      const { error: requestError } = await patchCompetitionTeam({
        path: { competitionId, teamId: team.id },
        body: { administration: { trackKey, registrationStatus: team.registrationStatus } },
      })
      if (requestError) throw requestError
      toast.success(translate("ui.teamTrackUpdated"))
      await load()
    }
    catch (requestError) {
      toast.error(competitionTrackErrorMessage(requestError, translate("ui.failedToUpdateTeamTrack")))
    }
    finally {
      pendingId.value = null
    }
  }

  function assignTrackValue(team: NoCtfapiEndpointsTeamsTeamResponse, value: unknown): void {
    if (typeof value === 'string') void assignTrack(team, value)
  }

  async function simpleAction(team: NoCtfapiEndpointsTeamsTeamResponse, action: 'approve' | 'reject') {
    if (!team.id || !team.trackKey) return
    pendingId.value = team.id
    try {
      const path = { competitionId, teamId: team.id }
      const { error } = await patchCompetitionTeam({
        path,
        body: {
          administration: {
            trackKey: team.trackKey,
            registrationStatus: action === 'approve' ? 'Approved' : 'Rejected',
          },
        },
      })
      if (error) throw error
      toast.success(translate("ui.operationCompleted"))
      await load()
    }
    catch (e) {
      toast.error(parseApiError(e).message)
    }
    finally {
      pendingId.value = null
    }
  }

  const banDialog = ref<{ team: NoCtfapiEndpointsTeamsTeamResponse; mode: 'ban' | 'correct' } | null>(null)

  const banReason = ref('')

  const banAnnouncePublicly = ref(false)

  const banPending = ref(false)

  const banReasonValid = computed(() => {
    const length = banReason.value.trim().length
    return length <= 512 && (banDialog.value?.mode === 'correct' ? length >= 8 : length > 0)
  })

  function openBan(team: NoCtfapiEndpointsTeamsTeamResponse, mode: 'ban' | 'correct') {
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
      const { error } = ctx.mode === 'ban'
        ? await patchCompetitionTeam({
            path,
            body: {
              ban: {
                isBanned: true,
                reason: banReason.value.trim(),
                announcePublicly: banAnnouncePublicly.value,
              },
            },
          })
        : await adminCorrectTeamBan({ path, body: { reason: banReason.value.trim() } })
      if (error) throw error
      toast.success(ctx.mode === 'ban' ? translate("ui.teamHasBeenBanned") : translate("ui.theBanHasBeenCorrected"))
      banDialog.value = null
      await load()
    }
    catch (e) {
      toast.error(parseApiError(e).message)
    }
    finally {
      banPending.value = false
    }
  }

  const appeals = ref<NoCtfapiEndpointsAdministrationTeamsAdminTeamBanCaseResponse[]>([])

  const appealsLoading = ref(true)

  const appealsError = ref<string | null>(null)

  const appealDialog = ref<{ banCase: NoCtfapiEndpointsAdministrationTeamsAdminTeamBanCaseResponse; mode: 'accept' | 'uphold' } | null>(null)

  const appealReason = ref('')

  const appealPending = ref(false)

  async function loadAppeals() {
    appealsLoading.value = true
    const { data, error } = await adminListTeamBanAppeals({ path: { competitionId } })
    if (error || !data) {
      appealsError.value = parseApiError(error).message
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

  function openAppeal(banCase: NoCtfapiEndpointsAdministrationTeamsAdminTeamBanCaseResponse, mode: 'accept' | 'uphold') {
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
      const { error } = await adminResolveTeamBanAppeal({
        path,
        body: {
          resolution: ctx.mode === 'accept' ? 'Accepted' : 'Upheld',
          reason: appealReason.value.trim(),
        },
      })
      if (error) throw error
      toast.success(ctx.mode === 'accept' ? translate("ui.appealAcceptedTeamUnblocked") : translate("ui.theAppealHasBeenDismissedAndTheBanIsMaintained"))
      appealDialog.value = null
      await Promise.all([load(), loadAppeals()])
    }
    catch (e) {
      toast.error(parseApiError(e).message)
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
      competitionId,
      canJudge,
      canWrite,
      teams,
      loading,
      error,
      pendingId,
      tracks,
      selectedTeam,
      teamMembers,
      teamDetailLoading,
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
      simpleAction,
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
     if (!open) viewState.selectedTeam = null
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
