
import { ResponseMetadata } from '../../lib/api'

import { api, RequestPolicyOption } from '../../lib/api'
import { message as describeMessage } from '../../utils/i18n'
import type { UiMessage } from '../../utils/i18n'
import { markRaw } from 'vue'

import { canEnterCompetition, canRegisterForCompetition, isCtfPracticeOpen } from '../../lib/competition-participation'
import { toast } from '../../utils/message-toast'
import { ArrowRight, Box, CalendarRange, Clock, EyeOff, FileText, KeyRound, LogIn, Settings, ShieldCheck, Trophy, UserPlus, Users } from '@lucide/vue'

import type { NoCTFAPIEndpointsAdministrationCompetitionsAdminCompetitionResponse, NoCTFAPIEndpointsCompetitionsCompetitionResponse, NoCTFAPIEndpointsCompetitionsTracksCompetitionTrackResponse, NoCTFAPIEndpointsTeamsTeamResponse } from '../../api/models'
import { teamMembershipErrorMessage, teamRegistrationErrorMessage } from '../../lib/competition-track'
import LifecycleBadgeComponent from './LifecycleBadge.vue'
import { useCompetitionPoster } from './useCompetitionPoster'

/** Owns state, effects and commands for CompetitionOverview. */
export function useCompetitionOverview(
  props: Readonly<{ competition: NoCTFAPIEndpointsCompetitionsCompetitionResponse }>,
  emit: (event: 'audienceChanged') => void,
) {
  const competitionId = props.competition.id!
  const { user, isLoggedIn, isAdministrator } = useAuth()
  const competition = ref(props.competition)
  const managementOnly = computed(() => isAdministrator.value && (competition.value.status === 'Draft' || Boolean(competition.value.deletedAt)))
  const detailError = ref<UiMessage | null>(null)
  const reads = new AbortController()
  onUnmounted(() => reads.abort())
  const protectedPoster = useCompetitionPoster(competitionId)
  const posterUrl = computed(() => competition.value.accessMode === 'StaffOnly'
    ? protectedPoster.posterUrl.value : competition.value.posterUrl ?? null)
  let posterActorId = user.value?.userId
  watch([() => competition.value.accessMode, () => competition.value.posterUrl, () => user.value?.userId], () => {
    if (posterActorId !== user.value?.userId) protectedPoster.cancelPoster()
    posterActorId = user.value?.userId
    if (competition.value.accessMode === 'StaffOnly' && competition.value.posterUrl && user.value) void protectedPoster.refreshPoster()
    else protectedPoster.cancelPoster()
  }, { immediate: true })
  async function refreshCompetition() {
    detailError.value = null
    try {
      let error: unknown;
      const data = await (managementOnly.value
        ? api.api.v1.admin.competitions.byCompetitionId(competitionId).get({ options: [new RequestPolicyOption({ signal: reads.signal })] })
        : api.api.v1.competitions.byCompetitionId(competitionId).get({ options: [new RequestPolicyOption({ signal: reads.signal })] })).catch(cause => { error = cause; return undefined });
      if (reads.signal.aborted) return
      if (error || !data) detailError.value = parseApiError(error).displayMessage
      else {
        competition.value = managementOnly.value
          ? (data as NoCTFAPIEndpointsAdministrationCompetitionsAdminCompetitionResponse).competition!
          : data as NoCTFAPIEndpointsCompetitionsCompetitionResponse
      }
    } catch (error) {
      if (!reads.signal.aborted) detailError.value = parseApiError(error).displayMessage
    }
  }
  onMounted(refreshCompetition)
  let unwatch: (() => void) | undefined
  onMounted(() => {
    unwatch = watchCompetition(competitionId, {
      competitionLifecycleChanged: () => void refreshCompetition(),
      competitionEventChanged: (event) => {
        if (event.kind === 'CompetitionAudienceChanged') {
          emit('audienceChanged')
          return
        }
        if (event.kind === 'TrackConfigurationUpdated'
          || event.kind === 'TrackRegistrationPolicyUpdated') {
          void refreshCompetition().then(loadRegistrationOptions)
        }
        if (event.kind === 'TeamTrackChanged') void loadMyTeam()
      },
      onReconnected: () => void refreshCompetition(),
    })
  })
  onUnmounted(() => unwatch?.())

  const myTeam = ref<NoCTFAPIEndpointsTeamsTeamResponse | null>(null)

  const teamLoaded = ref(false)

  const teamLoadError = ref<UiMessage | null>(null)

  async function loadMyTeam() {
    teamLoaded.value = false
    teamLoadError.value = null
    if (managementOnly.value) {
      myTeam.value = null
      teamLoaded.value = true
      return
    }
    if (!isLoggedIn.value) {
      myTeam.value = null
      teamLoaded.value = true
      return
    }
    try {
      let error: unknown;
      const response = new ResponseMetadata();
      const data = await api.api.v1.competitions.byCompetitionId(competitionId).teams.me.get({ options: [new RequestPolicyOption({ signal: reads.signal, response: response })] }).catch(cause => { error = cause; return undefined });
      if (response?.status === 404) {
        myTeam.value = null
        return
      }
      if (error || !data) {
        myTeam.value = null
        teamLoadError.value = parseApiError(error, describeMessage("competitions.competitionOverview.error.loadRegistrationStatusFailed")).displayMessage
        return
      }
      myTeam.value = data
    } catch (error: unknown) {
      myTeam.value = null
      teamLoadError.value = parseApiError(error, describeMessage("competitions.competitionOverview.error.loadRegistrationStatusFailed")).displayMessage
    } finally {
      teamLoaded.value = true
    }
  }

  onMounted(loadMyTeam)

  watch(isLoggedIn, loadMyTeam)

  const approvedTeamCount = ref<number | null>(null)

  const selectableTracks = ref<NoCTFAPIEndpointsCompetitionsTracksCompetitionTrackResponse[]>([])

  const tracksLoaded = ref(false)

  const trackLoadError = ref<UiMessage | null>(null)

  async function loadRegistrationOptions() {
    tracksLoaded.value = false
    trackLoadError.value = null
    if (managementOnly.value) {
      approvedTeamCount.value = null
      selectableTracks.value = []
      tracksLoaded.value = true
      return
    }
    try {
      const settledRequests = await Promise.allSettled([
        api.api.v1.competitions.byCompetitionId(competitionId).teams.get({ options: [new RequestPolicyOption({ signal: reads.signal })] }),
        tracksEnabled.value
          ? api.api.v1.competitions.byCompetitionId(competitionId).tracks.get({ options: [new RequestPolicyOption({ signal: reads.signal })] })
          : Promise.resolve(null),
      ]);
      const teams = settledRequests[0].status === 'fulfilled' ? settledRequests[0].value : undefined;
      const teamsError = settledRequests[0].status === 'rejected' ? settledRequests[0].reason : undefined;
      const trackResult = settledRequests[1].status === 'fulfilled' ? settledRequests[1].value : undefined;
      const trackResultError = settledRequests[1].status === 'rejected' ? settledRequests[1].reason : undefined;

      if (!teamsError && teams) approvedTeamCount.value = (teams.items ?? []).filter(
        (team) => team.registrationStatus === 'Approved',
      ).length
      if (!tracksEnabled.value) {
        selectableTracks.value = []
      } else if (trackResultError || !trackResult) {
        selectableTracks.value = []
        trackLoadError.value = parseApiError(
          trackResultError,
          describeMessage("competitions.competitionOverview.error.loadCompetitionTracksFailed"),
        ).displayMessage
      } else {
        selectableTracks.value = (trackResult.items ?? []).filter(track => track.isPublicSelectable)
        if (selectableTracks.value.some(track =>
            track.key === createTrackKey.value
            && track.meetsSsoRequirement === false)) {
          createTrackKey.value = ''
        }
      }
    } catch (error: unknown) {
      selectableTracks.value = []
      trackLoadError.value = parseApiError(
        error,
        describeMessage("competitions.competitionOverview.error.loadCompetitionTracksFailed"),
      ).displayMessage
    } finally {
      tracksLoaded.value = true
    }
  }

  onMounted(loadRegistrationOptions)

  watch(() => competition.value.tracksEnabled, () => void loadRegistrationOptions())

  const now = ref(Date.now())

  let timer: ReturnType<typeof setInterval> | undefined

  onMounted(() => {
    timer = setInterval(() => {
      now.value = Date.now()
    }, 1_000)
  })

  onUnmounted(() => clearInterval(timer))

  const countdown = computed(() => {
    const c = competition.value
    if (!c) return null
    const end = new Date(c.endTime ?? '').getTime()
    if (Number.isNaN(end)) return null
    if (now.value < end) return { label: translate("competitions.label.end"), ms: end - now.value }
    return { label: translate("common.label.finished"), ms: 0 }
  })

  const practiceOpen = computed(() => isCtfPracticeOpen(competition.value))

  const canParticipate = computed(() => canEnterCompetition(competition.value, myTeam.value))

  const teamRegistrationOpen = computed(() => canRegisterForCompetition(competition.value))

  const tracksEnabled = computed(() => competition.value.tracksEnabled ?? false)

  const createOpen = ref(false)

  const createName = ref('')

  const createTrackKey = ref('')

  const createPending = ref(false)

  const createValidationError = ref<UiMessage | null>(null)

  const selectedCreateTrack = computed(() => selectableTracks.value.find(
    track => track.key === createTrackKey.value,
  ))
  watch(createTrackKey, () => {
    createValidationError.value = null
  })

  watch(createOpen, (open) => {
    if (!open) createValidationError.value = null
  })

  async function submitCreate() {
    createValidationError.value = null
    if (!createName.value.trim()) {
      createValidationError.value = describeMessage("competitions.label.enterTeamName")
      return
    }
    if (tracksEnabled.value && !createTrackKey.value) {
      createValidationError.value = describeMessage("competitions.label.selectCompetitionTrack")
      return
    }
    createPending.value = true
    try {
      let error: unknown;
      const data = await api.api.v1.competitions.byCompetitionId(competitionId).teams.post({
          name: createName.value.trim(),
          ...(tracksEnabled.value
            ? {
                trackKey: createTrackKey.value,
                trackInvitationCode: null,
              }
            : {}),
        }).catch(cause => { error = cause; return undefined });
      if (error || !data) {
        createValidationError.value = teamRegistrationErrorMessage(
          error,
          translate("competitions.error.teamCreationFailed"),
        )
        return
      }
      toast.success(describeMessage("competitions.label.teamCreatedUnregistered"))
      createOpen.value = false
      createName.value = ''
      createTrackKey.value = ''
      await loadMyTeam()
    } catch (error: unknown) {
      createValidationError.value = teamRegistrationErrorMessage(
        error,
        translate("competitions.error.teamCreationFailed"),
      )
    } finally {
      createPending.value = false
    }
  }

  const joinOpen = ref(false)

  const joinToken = ref('')

  const joinPending = ref(false)

  const joinValidationError = ref<UiMessage | null>(null)

  watch(joinToken, () => { joinValidationError.value = null })

  watch(joinOpen, (open) => { if (!open) joinValidationError.value = null })

  async function submitJoin() {
    if (joinPending.value) return
    joinValidationError.value = null
    const invitationToken = joinToken.value.trim()
    if (invitationToken.length !== 32) {
      joinValidationError.value = describeMessage("competitions.competitionOverview.validation.invitationCodeLength")
      return
    }
    joinPending.value = true
    try {
      let error: unknown;
      await api.api.v1.competitions.byCompetitionId(competitionId).teams.join.post({ invitationToken }).catch(cause => { error = cause; return undefined });
      if (error) {
        joinValidationError.value = teamMembershipErrorMessage(
          error,
          translate("competitions.competitionOverview.error.joinTeamFailed"),
        )
        return
      }
      toast.success(describeMessage("competitions.label.alreadyJoinedTeam"))
      joinOpen.value = false
      joinToken.value = ''
      await loadMyTeam()
    } catch (error: unknown) {
      joinValidationError.value = teamMembershipErrorMessage(
        error,
        translate("competitions.competitionOverview.error.joinTeamFailed"),
      )
    } finally {
      joinPending.value = false
    }
  }

  const isCaptain = computed(
    () => myTeam.value && user.value && myTeam.value.captainId === user.value.userId,
  )

  const registrationSubmissionOpen = computed(() => competition.value.status === 'Visible'
    || competition.value.status === 'Published'
    || competition.value.status === 'Running'
      && competition.value.allowTeamRegistrationWhileRunning === true)

  const requiresManualReview = computed(() =>
    competition.value.teamRegistrationAutoApprove !== true)

  const registrationOpen = ref(false)

  const registrationInvitationCode = ref('')

  const registrationPending = ref(false)

  const registrationError = ref<UiMessage | null>(null)

  const registrationTrack = computed(() => selectableTracks.value.find(
    track => track.key === myTeam.value?.trackKey,
  ))

  const canSubmitRegistration = computed(() => Boolean(
    isCaptain.value
    && myTeam.value
    && (myTeam.value.registrationStatus === 'Unregistered'
      || myTeam.value.registrationStatus === 'Rejected')
    && registrationSubmissionOpen.value,
  ))

  const registrationValid = computed(() => Boolean(
    canSubmitRegistration.value
    && (!tracksEnabled.value || tracksLoaded.value
      && !trackLoadError.value
      && registrationTrack.value)
    && registrationTrack.value?.meetsSsoRequirement !== false
    && (!registrationTrack.value?.requiresInvitationCode
      || registrationInvitationCode.value.trim()),
  ))

  function openRegistration(): void {
    registrationInvitationCode.value = ''
    registrationError.value = null
    registrationOpen.value = true
  }

  function setRegistrationOpen(open: boolean): void {
    if (registrationPending.value) return
    registrationOpen.value = open
    if (!open) registrationError.value = null
  }

  async function submitRegistration(): Promise<void> {
    if (!myTeam.value || !registrationValid.value) return
    registrationPending.value = true
    registrationError.value = null
    try {
      let error: unknown;
      const data = await api.api.v1.competitions.byCompetitionId(competitionId).teams.byTeamId(myTeam.value.id!).patch({ registration: {
          status: 'Pending',
          trackInvitationCode: registrationTrack.value?.requiresInvitationCode
            ? registrationInvitationCode.value.trim()
            : null,
        } }).catch(cause => { error = cause; return undefined });
      if (error || !data) {
        registrationError.value = teamRegistrationErrorMessage(
          error,
          translate('competitions.error.resubmitRegistrationFailed'),
        )
        return
      }
      myTeam.value = data
      registrationOpen.value = false
      toast.success(data.registrationStatus === 'Approved'
        ? translate('competitions.label.registrationSubmittedApproved')
        : translate('competitions.competitionOverview.description.registrationResubmittedAwaitingReview'))
    }
    catch (error: unknown) {
      registrationError.value = teamRegistrationErrorMessage(
        error,
        translate('competitions.error.resubmitRegistrationFailed'),
      )
    }
    finally {
      registrationPending.value = false
    }
  }

  const LifecycleBadge = markRaw(LifecycleBadgeComponent)


  return {
      ArrowRight,
      Box,
      CalendarRange,
      Clock,
      EyeOff,
      FileText,
      KeyRound,
      LogIn,
      Settings,
      ShieldCheck,
      Trophy,
      UserPlus,
      Users,
      detailError,
      posterUrl,
      refreshCompetition,
      competitionId,
      isLoggedIn,
      isAdministrator,
      managementOnly,
      competition,
      myTeam,
      teamLoaded,
      teamLoadError,
      loadMyTeam,
      approvedTeamCount,
      selectableTracks,
      tracksLoaded,
      trackLoadError,
      loadRegistrationOptions,
      countdown,
      practiceOpen,
      canParticipate,
      teamRegistrationOpen,
      tracksEnabled,
      createOpen,
      createName,
      createTrackKey,
      createPending,
      createValidationError,
      selectedCreateTrack,
      submitCreate,
      joinOpen,
      joinToken,
      joinPending,
      joinValidationError,
      submitJoin,
      isCaptain,
      requiresManualReview,
      registrationOpen,
      registrationInvitationCode,
      registrationPending,
      registrationError,
      registrationTrack,
      canSubmitRegistration,
      registrationValid,
      openRegistration,
      setRegistrationOpen,
      submitRegistration,
      LifecycleBadge,
    }
}

export type CompetitionOverviewViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useCompetitionOverview>>>
