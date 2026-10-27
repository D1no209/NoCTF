import { computed, onScopeDispose, ref, watch } from 'vue'
import { getLiveSoloDirectionPool, listLiveSoloDirectionPresets, saveLiveSoloDirectionPreset, deleteLiveSoloDirectionPreset,
  configureLiveSoloDirectionPool, drawLiveSoloDirection, requestLiveSoloRescue, resolveLiveSoloRescue, listLiveSoloQuestionGroups } from '~/api'
import type { NoCtfapiEndpointsLiveSoloLiveSoloDirectionPoolResponse as Pool,
  NoCtfapiEndpointsLiveSoloLiveSoloDirectionPresetResponse as Preset,
  NoCtfapiEndpointsLiveSoloLiveSoloMatchResponse as Match, NoCtfapiEndpointsLiveSoloLiveSoloRoundResponse as Round } from '~/api'
import { message, type UiMessage } from '~/utils/i18n'
import { parseLiveSoloError } from './live-solo-errors'

export interface LiveSoloMatchDirectionsProps {
  competitionId: string
  match: Match
  round: Round | null
  staff: boolean
  teamId?: string
  members: { id: string; name: string }[]
}

export function useLiveSoloMatchDirections(props: LiveSoloMatchDirectionsProps, changed: () => void) {
  const state = ref<Pool | null>(null), presets = ref<Preset[]>([])
  const groups = ref<{ value: string; label: string }[]>([]), selectedIds = ref<string[]>([])
  const busy = ref(false), error = ref<UiMessage | null>(null), editorOpen = ref(false)
  const presetId = ref<string | null>(null), presetName = ref(''), direction = ref(''), questionGroupId = ref('')
  const rescueUserId = ref(''), rescueReason = ref(''), decisionReasons = ref<Record<string, string>>({})
  let disposed = false, request = 0
  let poolEdited = false
  const suggestedSlots = computed(() => 2 * (props.match.requiredWins ?? 2) - 1)
  const remaining = computed(() => state.value?.pool?.filter(x => x.available).length ?? 0)
  const selectedCount = computed(() => selectedIds.value.length)
  const poolRows = computed(() => (state.value?.pool ?? []).map(row => ({ ...row,
    label: row.roundNumber != null ? message('liveSolo.draw.drawnRound', { number: row.roundNumber })
      : message(row.available ? 'liveSolo.draw.remaining' : 'liveSolo.draw.alreadyUsed') })))
  const roster = computed(() => props.match.rosters?.find(x => x.teamId === props.teamId))
  const rescueOptions = computed(() => props.members.filter(x => !roster.value?.userIds?.includes(x.id))
    .map(x => ({ value: x.id, label: x.name })))
  const requests = computed(() => (state.value?.rescues ?? []).map(row => ({ ...row,
    stateKey: row.state === 'Approved' ? 'liveSolo.rescue.approved' as const : row.state === 'Rejected' ? 'liveSolo.rescue.rejected' as const
      : row.roundId === props.round?.id && state.value?.canJudgeRescue ? 'liveSolo.rescue.pending' as const
        : row.roundId !== props.round?.id || !['Running', 'Paused'].includes(props.match.state ?? '') ? 'liveSolo.rescue.expired' as const : 'liveSolo.rescue.pending' as const,
    mayResolve: row.state === 'Pending' && row.roundId === props.round?.id && state.value?.canJudgeRescue === true,
  })))
  const presetRows = computed(() => presets.value.map(row => ({ ...row, selected: !!row.id && selectedIds.value.includes(row.id) })))
  const validPreset = computed(() => !!direction.value.trim() && !!questionGroupId.value)

  async function load() {
    const generation = ++request, path = { competitionId: props.competitionId, matchId: props.match.id! }
    if (!path.matchId) return
    try {
      const response = await getLiveSoloDirectionPool({ path, query: { staff: props.staff } })
      if (disposed || generation !== request) return
      if (response.error || !response.data) throw parseLiveSoloError(response.error, message('liveSolo.error.load'))
      state.value = response.data
      if (props.staff && (response.data.canConfigure || response.data.canManagePresets)) {
        const [catalog, lists] = await Promise.all([listLiveSoloDirectionPresets({ path }), listLiveSoloQuestionGroups({ path })])
        if (disposed || generation !== request) return
        if (catalog.error || lists.error) throw parseLiveSoloError(catalog.error ?? lists.error, message('liveSolo.error.load'))
        presets.value = catalog.data?.items ?? []
        groups.value = (lists.data?.items ?? []).filter(x => x.id).map(x => ({ value: x.id!, label: x.name ?? '—' }))
        selectedIds.value = selectedIds.value.filter(id => presets.value.some(x => x.id === id))
        if (!poolEdited) selectedIds.value = (state.value?.pool ?? []).filter(x => !x.drawnAt).map(x =>
          presets.value.find(preset => preset.questionGroupId === x.questionGroupId)?.id).filter((id): id is string => !!id)
      } else { presets.value = []; groups.value = []; editorOpen.value = false }
      error.value = null
    } catch (cause) {
      if (!disposed && generation === request) {
        state.value = null; presets.value = []; groups.value = []
        error.value = parseLiveSoloError(cause, message('liveSolo.error.load')).displayMessage
      }
    }
  }
  async function mutate(work: () => PromiseLike<{ error?: unknown; response?: Response }>) {
    if (busy.value || !state.value) return
    busy.value = true; error.value = null
    let failure: UiMessage | null = null
    try {
      const response = await work()
      if (response.error || response.response?.ok === false) throw parseLiveSoloError(response.error, message('liveSolo.error.operation'))
      changed()
    } catch (cause) { failure = parseLiveSoloError(cause, message('liveSolo.error.operation')).displayMessage }
    finally { busy.value = false; await load(); if (failure) error.value = failure }
  }
  function togglePreset(id: string, selected: boolean | 'indeterminate') {
    poolEdited = true
    if (selected === true && !selectedIds.value.includes(id)) selectedIds.value = [...selectedIds.value, id]
    else if (selected !== true) selectedIds.value = selectedIds.value.filter(value => value !== id)
  }
  function editPreset(preset?: Preset) {
    presetId.value = preset?.id ?? null; presetName.value = preset?.name ?? ''; direction.value = preset?.direction ?? ''
    questionGroupId.value = preset?.questionGroupId ?? ''; editorOpen.value = true
  }
  function closeEditor() { editorOpen.value = false }
  async function savePreset() {
    if (!state.value?.canManagePresets || !validPreset.value) return
    await mutate(() => saveLiveSoloDirectionPreset({ path: { competitionId: props.competitionId }, body: { id: presetId.value,
      name: presetName.value.trim(), direction: direction.value.trim(), questionGroupId: questionGroupId.value } }))
    if (!error.value) editorOpen.value = false
  }
  async function removePreset(id: string) {
    if (!state.value?.canManagePresets) return
    await mutate(() => deleteLiveSoloDirectionPreset({ path: { competitionId: props.competitionId, presetId: id } }))
  }
  async function configure(clear = false) {
    if (!state.value?.canConfigure) return
    await mutate(() => configureLiveSoloDirectionPool({ path: { competitionId: props.competitionId, matchId: props.match.id! }, body: { presetIds: clear ? [] : selectedIds.value } }))
    if (!error.value) { poolEdited = false; await load() }
  }
  async function draw() {
    if (!state.value?.canDraw) return
    await mutate(() => drawLiveSoloDirection({ path: { competitionId: props.competitionId, matchId: props.match.id! } }))
  }
  async function requestRescue() {
    if (!state.value?.canRequestRescue || !props.round?.id || !rescueUserId.value) return
    await mutate(() => requestLiveSoloRescue({ path: { competitionId: props.competitionId, matchId: props.match.id!, roundId: props.round!.id! },
      body: { userId: rescueUserId.value, reason: rescueReason.value.trim() } }))
    if (!error.value) { rescueReason.value = ''; rescueUserId.value = '' }
  }
  async function resolve(id: string, approve: boolean) {
    const reason = decisionReasons.value[id]?.trim() ?? ''
    if (!state.value?.canJudgeRescue) return
    await mutate(() => resolveLiveSoloRescue({ path: { competitionId: props.competitionId, matchId: props.match.id!, requestId: id }, body: { approve, reason } }))
  }
  watch(() => [props.competitionId, props.match.id, props.staff], () => {
    state.value = null; selectedIds.value = []; poolEdited = false; decisionReasons.value = {}; closeEditor(); void load()
  }, { immediate: true })
  watch(() => [props.match.concurrencyStamp, props.round?.state], () => { void load() })
  onScopeDispose(() => { disposed = true; request++ })
  return { state, presets: presetRows, groups, selectedCount, suggestedSlots, remaining, poolRows, busy, error, editorOpen, presetName, direction, questionGroupId,
    rescueUserId, rescueReason, decisionReasons, rescueOptions, requests, validPreset, togglePreset, editPreset, closeEditor, savePreset, removePreset, configure, draw, requestRescue, resolve }
}
export type LiveSoloMatchDirectionsState = import('vue').ShallowUnwrapRef<ReturnType<typeof useLiveSoloMatchDirections>>
