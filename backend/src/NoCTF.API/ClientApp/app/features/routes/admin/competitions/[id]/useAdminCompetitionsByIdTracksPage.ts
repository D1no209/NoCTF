

import { Plus, Save, Trash2 } from '@lucide/vue'
import { toast } from 'vue-sonner'
import { adminGetCompetition, adminPatchCompetition } from '../../../../../api'
import type { NoCtfapiEndpointsAdministrationCompetitionsUpdateCompetitionTrackRequest } from '../../../../../api'
import { useCompetitionAdmin } from '../../../../../lib/admin-competition'
import { competitionTrackErrorMessage } from '../../../../../lib/competition-track'

type TrackForm = Omit<NoCtfapiEndpointsAdministrationCompetitionsUpdateCompetitionTrackRequest, 'invitationCode'> & {
  clientId: string
  requiresInvitationCode: boolean
  invitationCodeConfigured: boolean
  invitationCode: string
}

/** Owns state, effects and commands for AdminCompetitionsByIdTracksPage. */
export function useAdminCompetitionsByIdTracksPage() {
  const { competitionId, canWrite } = useCompetitionAdmin()

  const mode = ref<'Ctf' | 'Awd' | 'Awdp' | 'Koh'>('Ctf')

  const frozen = ref(false)

  const tracks = ref<TrackForm[]>([])

  const loading = ref(true)

  const saving = ref(false)

  const error = ref<string | null>(null)

  async function load() {
    loading.value = true
    const { data, error: requestError } = await adminGetCompetition({ path: { competitionId } })
    loading.value = false
    if (requestError || !data) {
      error.value = competitionTrackErrorMessage(requestError, translate("ui.failedToLoadTrackConfiguration"))
      return
    }
    mode.value = data.competition?.mode ?? 'Ctf'
    frozen.value = data.tracks?.isFrozen ?? false
    tracks.value = (data.tracks?.items ?? []).map(item => ({
      clientId: crypto.randomUUID(),
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
    }))
    error.value = null
  }

  function addTrack() {
    const ordinal = tracks.value.length + 1
    tracks.value.push({
      clientId: crypto.randomUUID(),
      key: `track-${ordinal}`,
      name: translate("ui.newTrack", { ordinal }),
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
    })
  }

  function removeTrack(index: number) {
    if (tracks.value[index]?.isDefault) return
    tracks.value.splice(index, 1)
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

  async function save() {
    if (saving.value || frozen.value || !canWrite.value) return
    const invalidInvitationTrack = tracks.value.find(track =>
      track.requiresInvitationCode
      && !track.invitationCodeConfigured
      && !track.invitationCode?.trim(),
    )
    if (invalidInvitationTrack) {
      const trackName = invalidInvitationTrack.name?.trim() || invalidInvitationTrack.key?.trim() || translate("ui.unnamedTrack")
      error.value = translate("ui.trackRequiresAnInvitationCode", { name: trackName })
      toast.error(error.value)
      return
    }
    saving.value = true
    const { data, error: requestError } = await adminPatchCompetition({
      path: { competitionId },
      body: {
        tracks: {
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
            invitationCode: track.requiresInvitationCode ? track.invitationCode.trim() : null,
            clearInvitationCode: track.clearInvitationCode,
          })),
        },
      },
    })
    saving.value = false
    if (requestError || !data) {
      error.value = competitionTrackErrorMessage(requestError, translate("ui.failedToSaveTrackConfiguration"))
      toast.error(error.value)
      return
    }
    toast.success(translate("ui.trackConfigurationSaved"))
    await load()
  }

  onMounted(load)

  return {
      Plus,
      Save,
      Trash2,
      canWrite,
      mode,
      frozen,
      tracks,
      loading,
      saving,
      error,
      load,
      addTrack,
      removeTrack,
      setDefault,
      setPublicSelectable,
      setInternal,
      setInvitationRequired,
      updateDefault,
      updatePublicSelectable,
      updateInternal,
      updateInvitationRequired,
      save
    }
}

export type AdminCompetitionsByIdTracksPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useAdminCompetitionsByIdTracksPage>>>
