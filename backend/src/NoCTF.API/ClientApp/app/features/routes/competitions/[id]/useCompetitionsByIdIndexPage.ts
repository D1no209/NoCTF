import { markRaw } from 'vue'

import { canEnterCompetition, isCtfPracticeOpen } from '../../../../lib/competition-participation'
import { toast } from 'vue-sonner'
import { ArrowRight, Box, CalendarRange, Clock, FileText, KeyRound, LogIn, ShieldCheck, Trophy, UserPlus, Users } from '@lucide/vue'
import { createTeamEndpoint, getMyTeamEndpoint, joinTeamByInvitationEndpoint, listCompetitionTeamsEndpoint, listCompetitionTracks } from '../../../../api'
import type { NoCtfapiEndpointsCompetitionsTracksCompetitionTrackResponse, NoCtfapiEndpointsTeamsTeamResponse } from '../../../../api'
import { teamMembershipErrorMessage, teamRegistrationErrorMessage } from '../../../../lib/competition-track'
import LifecycleBadgeComponent from '../../../competitions/LifecycleBadge.vue'
import ModeBadgeComponent from '../../../competitions/ModeBadge.vue'

/** Owns state, effects and commands for CompetitionsByIdIndexPage. */
export function useCompetitionsByIdIndexPage() {
  const route = useRoute()

  const competitionId = route.params.id as string

  const ctx = inject(competitionContextKey)!

  const { user, isLoggedIn } = useAuth()

  const competition = computed(() => ctx.competition.value)

  const myTeam = ref<NoCtfapiEndpointsTeamsTeamResponse | null>(null)

  const teamLoaded = ref(false)

  const teamLoadError = ref<string | null>(null)

  async function loadMyTeam() {
    teamLoaded.value = false
    teamLoadError.value = null
    if (!isLoggedIn.value) {
      myTeam.value = null
      teamLoaded.value = true
      return
    }
    try {
      const { data, error, response } = await getMyTeamEndpoint({ path: { competitionId } })
      if (response?.status === 404) {
        myTeam.value = null
        return
      }
      if (error || !data) {
        myTeam.value = null
        teamLoadError.value = parseApiError(error, translate("ui.failedToLoadYourRegistrationStatusPleaseTryAgain")).message
        return
      }
      myTeam.value = data
    } catch (error: unknown) {
      myTeam.value = null
      teamLoadError.value = parseApiError(error, translate("ui.failedToLoadYourRegistrationStatusPleaseTryAgain")).message
    } finally {
      teamLoaded.value = true
    }
  }

  onMounted(loadMyTeam)

  watch(isLoggedIn, loadMyTeam)

  const approvedTeamCount = ref<number | null>(null)

  const selectableTracks = ref<NoCtfapiEndpointsCompetitionsTracksCompetitionTrackResponse[]>([])

  const tracksLoaded = ref(false)

  const trackLoadError = ref<string | null>(null)

  async function loadRegistrationOptions() {
    tracksLoaded.value = false
    trackLoadError.value = null
    try {
      const [teams, tracks] = await Promise.all([
        listCompetitionTeamsEndpoint({ path: { competitionId } }),
        listCompetitionTracks({ path: { competitionId } }),
      ])
      if (!teams.error && teams.data) approvedTeamCount.value = (teams.data.items ?? []).filter(
        (team) => team.registrationStatus === 'Approved',
      ).length
      if (tracks.error || !tracks.data) {
        selectableTracks.value = []
        trackLoadError.value = parseApiError(
          tracks.error,
          translate("ui.failedToLoadCompetitionTracksPleaseTryAgain"),
        ).message
      } else {
        selectableTracks.value = (tracks.data.items ?? []).filter(track => track.isPublicSelectable)
      }
    } catch (error: unknown) {
      selectableTracks.value = []
      trackLoadError.value = parseApiError(
        error,
        translate("ui.failedToLoadCompetitionTracksPleaseTryAgain"),
      ).message
    } finally {
      tracksLoaded.value = true
    }
  }

  onMounted(loadRegistrationOptions)

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
    const start = new Date(c.startTime ?? '').getTime()
    const end = new Date(c.endTime ?? '').getTime()
    if (Number.isNaN(start) || Number.isNaN(end)) return null
    if (now.value < start) return { label: translate("ui.fromStart"), ms: start - now.value }
    if (now.value < end) return { label: translate("ui.fromTheEnd"), ms: end - now.value }
    return { label: translate("ui.finished"), ms: 0 }
  })

  const practiceOpen = computed(() => isCtfPracticeOpen(competition.value))

  const canParticipate = computed(() => canEnterCompetition(competition.value, myTeam.value))

  const teamRegistrationOpen = computed(() => {
    const status = competition.value?.status
    return status === 'Visible'
      || status === 'Published'
      || status === 'Running'
        && competition.value?.allowTeamRegistrationWhileRunning === true
  })

  const createOpen = ref(false)

  const createName = ref('')

  const createTrackKey = ref('')

  const createTrackInvitationCode = ref('')

  const createPending = ref(false)

  const createValidationError = ref<string | null>(null)

  const selectedCreateTrack = computed(() => selectableTracks.value.find(
    track => track.key === createTrackKey.value,
  ))

  watch(createTrackKey, () => {
    createTrackInvitationCode.value = ''
    createValidationError.value = null
  })

  watch(createOpen, (open) => {
    if (!open) createValidationError.value = null
  })

  async function submitCreate() {
    createValidationError.value = null
    if (!createName.value.trim()) {
      createValidationError.value = translate("ui.enterATeamName")
      return
    }
    if (!createTrackKey.value) {
      createValidationError.value = translate("ui.selectACompetitionTrack")
      return
    }
    if (selectedCreateTrack.value?.requiresInvitationCode
      && !createTrackInvitationCode.value.trim()) {
      createValidationError.value = translate("ui.enterTheTrackInvitationCode")
      return
    }
    createPending.value = true
    try {
      const { data, error } = await createTeamEndpoint({
        path: { competitionId },
        body: {
          name: createName.value.trim(),
          trackKey: createTrackKey.value,
          trackInvitationCode: selectedCreateTrack.value?.requiresInvitationCode
            ? createTrackInvitationCode.value.trim()
            : null,
        },
      })
      if (error || !data) {
        createValidationError.value = teamRegistrationErrorMessage(
          error,
          translate("ui.teamCreationFailed"),
        )
        return
      }
      toast.success(translate("ui.teamCreatedSuccessfully"))
      createOpen.value = false
      createName.value = ''
      createTrackKey.value = ''
      createTrackInvitationCode.value = ''
      await loadMyTeam()
    } catch (error: unknown) {
      createValidationError.value = teamRegistrationErrorMessage(
        error,
        translate("ui.teamCreationFailed"),
      )
    } finally {
      createPending.value = false
    }
  }

  const joinOpen = ref(false)

  const joinToken = ref('')

  const joinPending = ref(false)

  const joinValidationError = ref<string | null>(null)

  watch(joinToken, () => { joinValidationError.value = null })

  watch(joinOpen, (open) => { if (!open) joinValidationError.value = null })

  async function submitJoin() {
    if (joinPending.value) return
    joinValidationError.value = null
    const invitationToken = joinToken.value.trim()
    if (invitationToken.length !== 32) {
      joinValidationError.value = translate("ui.theInvitationCodeMustBe32Characters")
      return
    }
    joinPending.value = true
    try {
      const { error } = await joinTeamByInvitationEndpoint({
        path: { competitionId },
        body: { invitationToken },
      })
      if (error) {
        joinValidationError.value = teamMembershipErrorMessage(
          error,
          translate("ui.failedToJoinTheTeamPleaseTryAgain"),
        )
        return
      }
      toast.success(translate("ui.alreadyJoinedTheTeam"))
      joinOpen.value = false
      joinToken.value = ''
      await loadMyTeam()
    } catch (error: unknown) {
      joinValidationError.value = teamMembershipErrorMessage(
        error,
        translate("ui.failedToJoinTheTeamPleaseTryAgain"),
      )
    } finally {
      joinPending.value = false
    }
  }

  const isCaptain = computed(
    () => myTeam.value && user.value && myTeam.value.captainId === user.value.userId,
  )

  const LifecycleBadge = markRaw(LifecycleBadgeComponent)

  const ModeBadge = markRaw(ModeBadgeComponent)

  return {
      ArrowRight,
      Box,
      CalendarRange,
      Clock,
      FileText,
      KeyRound,
      LogIn,
      ShieldCheck,
      Trophy,
      UserPlus,
      Users,
      competitionId,
      isLoggedIn,
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
      createOpen,
      createName,
      createTrackKey,
      createTrackInvitationCode,
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
      LifecycleBadge,
      ModeBadge
    }
}

export type CompetitionsByIdIndexPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useCompetitionsByIdIndexPage>>>
