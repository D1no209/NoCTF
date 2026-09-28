import { Plus, Save, Trash2 } from '@lucide/vue'
import { toast } from 'vue-sonner'
import { adminGetCompetition, adminListTeams, adminPatchCompetition } from '../../../../../api'
import type {
  NoCtfapiEndpointsAdministrationCompetitionsRemovedTrackReassignmentRequest,
  NoCtfapiEndpointsAdministrationCompetitionsCompetitionSsoProviderResponse,
  NoCtfapiEndpointsAdministrationCompetitionsUpdateCompetitionTrackRequest,
  NoCtfapiEndpointsTeamsTeamResponse,
} from '../../../../../api'
import { useCompetitionAdmin } from '../../../../../lib/admin-competition'
import {
  competitionTrackErrorMessage,
  duplicateCompetitionTrackKey,
  nextCompetitionTrackOrdinal,
} from '../../../../../lib/competition-track'

type TrackForm = Omit<NoCtfapiEndpointsAdministrationCompetitionsUpdateCompetitionTrackRequest, 'invitationCode'> & {
  clientId: string
  existingKey: string | null
  requiresInvitationCode: boolean
  invitationCodeConfigured: boolean
  invitationCode: string
}

interface PendingTrackRemoval {
  index: number
  key: string
  name: string
  affectedTeamCount: number
  toTrackKey: string
}

/** Owns state, effects and commands for AdminCompetitionsByIdTracksPage. */
export function useAdminCompetitionsByIdTracksPage() {
  const { competitionId, canWrite } = useCompetitionAdmin()

  const mode = ref<'Ctf' | 'Awd' | 'Awdp' | 'Koh'>('Ctf')
  const enabled = ref(false)
  const canUpdate = ref(false)
  const tracks = ref<TrackForm[]>([])
  const ssoProviders = ref<NoCtfapiEndpointsAdministrationCompetitionsCompetitionSsoProviderResponse[]>([])
  const teams = ref<NoCtfapiEndpointsTeamsTeamResponse[]>([])
  const removedTrackReassignments = ref<NoCtfapiEndpointsAdministrationCompetitionsRemovedTrackReassignmentRequest[]>([])
  const pendingRemoval = ref<PendingTrackRemoval | null>(null)
  const disableConfirmationOpen = ref(false)
  const loading = ref(true)
  const saving = ref(false)
  const error = ref<string | null>(null)

  const defaultTrack = computed(() => tracks.value.find(track => track.isDefault) ?? null)
  const removalTargets = computed(() => tracks.value.filter(track =>
    track.key && track.key !== pendingRemoval.value?.key,
  ))
  const disableAffectedTeamCount = computed(() => {
    const defaultKey = defaultTrack.value?.key
    return defaultKey
      ? teams.value.filter(team => team.trackKey !== defaultKey).length
      : teams.value.length
  })

  async function load() {
    loading.value = true
    const [competitionResult, teamResult] = await Promise.all([
      adminGetCompetition({ path: { competitionId } }),
      adminListTeams({ path: { competitionId }, query: { keyword: null, offset: 0, limit: 200, desc: false } }),
    ])
    loading.value = false
    if (competitionResult.error || !competitionResult.data) {
      error.value = competitionTrackErrorMessage(
        competitionResult.error,
        translate('ui.failedToLoadTrackConfiguration'),
      )
      return
    }
    if (teamResult.error || !teamResult.data) {
      error.value = parseApiError(teamResult.error, translate('ui.failedToLoadRegisteredTeams')).message
      return
    }

    const data = competitionResult.data
    mode.value = data.competition?.mode ?? 'Ctf'
    enabled.value = data.tracks?.enabled ?? data.competition?.tracksEnabled ?? false
    canUpdate.value = data.tracks?.canUpdate ?? false
    teams.value = teamResult.data.items ?? []
    ssoProviders.value = data.ssoProviders ?? []
    removedTrackReassignments.value = []
    pendingRemoval.value = null
    disableConfirmationOpen.value = false
    tracks.value = (data.tracks?.items ?? []).map(item => ({
      clientId: crypto.randomUUID(),
      existingKey: item.key ?? '',
      key: item.key ?? '',
      name: item.name ?? '',
      isDefault: item.isDefault ?? false,
      isPublicSelectable: item.isPublicSelectable ?? false,
      isInternal: item.isInternal ?? false,
      earnsScore: item.earnsScore ?? false,
      earnsBlood: item.earnsBlood ?? false,
      affectsDynamicChallengeScore: item.affectsDynamicChallengeScore ?? false,
      visibleOnLeaderboard: item.visibleOnLeaderboard ?? false,
      affectsCompetitiveResults: item.affectsCompetitiveResults ?? false,
      requiresInvitationCode: item.requiresInvitationCode ?? false,
      invitationCodeConfigured: item.requiresInvitationCode ?? false,
      invitationCode: '',
      clearInvitationCode: false,
      requiredSsoProviderId: item.requiredSsoProviderId ?? null,
    }))
    error.value = null
  }

  function addTrack() {
    const ordinal = nextCompetitionTrackOrdinal(tracks.value.map(track => track.key))
    tracks.value.push({
      clientId: crypto.randomUUID(),
      existingKey: null,
      key: `track-${ordinal}`,
      name: translate('ui.newTrack', { ordinal }),
      isDefault: false,
      isPublicSelectable: true,
      isInternal: false,
      earnsScore: true,
      earnsBlood: mode.value === 'Ctf',
      affectsDynamicChallengeScore: mode.value === 'Ctf',
      visibleOnLeaderboard: true,
      affectsCompetitiveResults: true,
      requiresInvitationCode: false,
      invitationCodeConfigured: false,
      invitationCode: '',
      clearInvitationCode: false,
      requiredSsoProviderId: null,
    })
  }

  function removeTrackAt(index: number) {
    const removedKey = tracks.value[index]?.key
    if (!removedKey || tracks.value[index]?.isDefault) return
    tracks.value.splice(index, 1)
    removedTrackReassignments.value = removedTrackReassignments.value.filter(
      reassignment => reassignment.fromTrackKey !== removedKey,
    )
  }

  function requestRemoveTrack(index: number) {
    const track = tracks.value[index]
    if (!track || track.isDefault) return
    if (!track.existingKey) {
      removeTrackAt(index)
      return
    }
    const affectedTeamCount = teams.value.filter(team => team.trackKey === track.existingKey).length
    pendingRemoval.value = {
      index,
      key: track.existingKey,
      name: track.name || track.existingKey,
      affectedTeamCount,
      toTrackKey: defaultTrack.value?.key === track.existingKey ? '' : defaultTrack.value?.key ?? '',
    }
  }

  function closeRemoval(open: boolean) {
    if (!open) pendingRemoval.value = null
  }

  function confirmRemoveTrack() {
    const removal = pendingRemoval.value
    if (!removal?.toTrackKey) return
    removedTrackReassignments.value = [
      ...removedTrackReassignments.value.filter(item => item.fromTrackKey !== removal.key),
      { fromTrackKey: removal.key, toTrackKey: removal.toTrackKey },
    ]
    tracks.value.splice(removal.index, 1)
    pendingRemoval.value = null
  }

  function requestEnabled(value: boolean) {
    if (value === enabled.value) return
    if (!value) {
      disableConfirmationOpen.value = true
      return
    }
    enabled.value = true
  }

  function confirmDisable() {
    enabled.value = false
    disableConfirmationOpen.value = false
    pendingRemoval.value = null
    removedTrackReassignments.value = []
  }

  function setDisableConfirmationOpen(open: boolean) {
    disableConfirmationOpen.value = open
  }

  function setDefault(index: number) {
    tracks.value.forEach((track, trackIndex) => { track.isDefault = trackIndex === index })
  }

  function clearInvitationRequirement(track: TrackForm) {
    track.requiresInvitationCode = false
    track.invitationCode = ''
    track.clearInvitationCode = track.invitationCodeConfigured
  }

  function setPublicSelectable(track: TrackForm, selectable: boolean) {
    track.isPublicSelectable = selectable
    if (!selectable) clearInvitationRequirement(track)
  }

  function setInternal(track: TrackForm, internal: boolean) {
    track.isInternal = internal
    if (!internal) return
    track.isPublicSelectable = false
    track.earnsScore = false
    track.earnsBlood = false
    track.affectsDynamicChallengeScore = false
    track.visibleOnLeaderboard = false
    track.affectsCompetitiveResults = false
    clearInvitationRequirement(track)
  }

  function setInvitationRequired(track: TrackForm, required: boolean) {
    track.requiresInvitationCode = required
    track.clearInvitationCode = !required && track.invitationCodeConfigured
    if (!required) {
      track.invitationCode = ''
      return
    }
    track.clearInvitationCode = false
  }

  function updateDefault(index: number, value: boolean | 'indeterminate'): void {
    if (value === true) setDefault(index)
  }

  function updatePublicSelectable(track: TrackForm, value: boolean | 'indeterminate'): void {
    setPublicSelectable(track, value === true)
  }

  function updateInternal(track: TrackForm, value: boolean | 'indeterminate'): void {
    setInternal(track, value === true)
  }

  function updateInvitationRequired(track: TrackForm, value: boolean | 'indeterminate'): void {
    setInvitationRequired(track, value === true)
  }

  function updateRequiredSsoProvider(track: TrackForm, providerId: string): void {
    track.requiredSsoProviderId = providerId === 'none' ? null : providerId
  }

  function configureSsoProviders(): void {
    void navigateTo('/admin/platform/authentication')
  }

  async function save() {
    if (saving.value || !canUpdate.value || !canWrite.value) return
    const duplicateTrackKey = duplicateCompetitionTrackKey(tracks.value.map(track => track.key))
    if (duplicateTrackKey) {
      error.value = translate('ui.duplicateTrackKey', { key: duplicateTrackKey })
      toast.error(error.value)
      return
    }
    const invalidInvitationTrack = tracks.value.find(track =>
      track.requiresInvitationCode
      && !track.invitationCodeConfigured
      && !track.invitationCode?.trim(),
    )
    if (invalidInvitationTrack) {
      const trackName = invalidInvitationTrack.name?.trim()
        || invalidInvitationTrack.key?.trim()
        || translate('ui.unnamedTrack')
      error.value = translate('ui.trackRequiresAnInvitationCode', { name: trackName })
      toast.error(error.value)
      return
    }
    saving.value = true
    const { data, error: requestError } = await adminPatchCompetition({
      path: { competitionId },
      body: {
        tracks: {
          enabled: enabled.value,
          tracks: tracks.value.map(track => ({
            key: track.key,
            name: track.name,
            isDefault: track.isDefault,
            isPublicSelectable: track.isPublicSelectable,
            isInternal: track.isInternal,
            earnsScore: track.earnsScore,
            earnsBlood: track.earnsBlood,
            affectsDynamicChallengeScore: track.affectsDynamicChallengeScore,
            visibleOnLeaderboard: track.visibleOnLeaderboard,
            affectsCompetitiveResults: track.affectsCompetitiveResults,
            invitationCode: track.requiresInvitationCode
              ? track.invitationCode.trim() || null
              : null,
            clearInvitationCode: track.clearInvitationCode,
            requiredSsoProviderId: track.requiredSsoProviderId ?? null,
          })),
          removedTrackReassignments: enabled.value ? removedTrackReassignments.value : [],
        },
      },
    })
    saving.value = false
    if (requestError || !data) {
      error.value = competitionTrackErrorMessage(requestError, translate('ui.failedToSaveTrackConfiguration'))
      toast.error(error.value)
      return
    }
    toast.success(translate('ui.trackConfigurationSaved'))
    await load()
  }

  onMounted(load)

  return {
    Plus,
    Save,
    Trash2,
    canWrite,
    mode,
    enabled,
    canUpdate,
    tracks,
    ssoProviders,
    pendingRemoval,
    removalTargets,
    disableConfirmationOpen,
    disableAffectedTeamCount,
    loading,
    saving,
    error,
    load,
    addTrack,
    requestRemoveTrack,
    closeRemoval,
    confirmRemoveTrack,
    requestEnabled,
    confirmDisable,
    setDisableConfirmationOpen,
    updateDefault,
    updatePublicSelectable,
    updateInternal,
    updateInvitationRequired,
    updateRequiredSsoProvider,
    configureSsoProviders,
    save,
  }
}

export type AdminCompetitionsByIdTracksPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useAdminCompetitionsByIdTracksPage>>>
