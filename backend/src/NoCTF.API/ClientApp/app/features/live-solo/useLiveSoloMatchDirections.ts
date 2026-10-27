import { computed, onScopeDispose, ref, watch } from 'vue'
import { getLiveSoloDirectionPool, listLiveSoloDirectionPresets, saveLiveSoloDirectionPreset, saveLiveSoloDirectionPresets, deleteLiveSoloDirectionPreset, adminGetCompetitionDirections,
  configureLiveSoloDirectionPool, drawLiveSoloDirection, requestLiveSoloRescue, resolveLiveSoloRescue, listLiveSoloQuestionGroups } from '../../api'
import type { NoCtfapiEndpointsLiveSoloLiveSoloDirectionPoolResponse as Pool,
  NoCtfapiEndpointsLiveSoloLiveSoloDirectionPresetResponse as Preset,
  NoCtfapiEndpointsLiveSoloLiveSoloMatchResponse as Match, NoCtfapiEndpointsLiveSoloLiveSoloRoundResponse as Round } from '../../api'
import { message, type UiMessage } from '../../utils/i18n'
import { parseLiveSoloError } from './live-solo-errors'
import { directionPoolCounts } from './direction-pool'

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
  const directionCatalog = ref<string[]>([]), questionGroupIds = ref<string[]>([]), groupSearch = ref('')
  const editingPreset = computed(() => presetId.value != null)
  const directionOptions = computed(() => [...new Set([...directionCatalog.value, ...presets.value.map(x => x.direction ?? ''), direction.value])]
    .filter(Boolean).map(value => ({ value, label: value })))
  const batchGroups = computed(() => groups.value.filter(x => x.label.toLocaleLowerCase().includes(groupSearch.value.trim().toLocaleLowerCase()))
    .map(x => ({ ...x, selected: questionGroupIds.value.includes(x.value) })))
  const batchSelectedCount = computed(() => questionGroupIds.value.length)
  const rescueUserId = ref(''), rescueReason = ref(''), decisionReasons = ref<Record<string, string>>({})
  let disposed = false, request = 0
  let poolEdited = false
  const suggestedSlots = computed(() => 2 * (props.match.requiredWins ?? 2) - 1)
  const remaining = computed(() => state.value?.pool?.filter(x => x.available).length ?? 0)
  const selectedCount = computed(() => selectedIds.value.length)
  const poolRows = computed(() => (state.value?.pool ?? []).map(row => ({ ...row,
    label: row.roundNumber != null ? message('liveSolo.draw.drawnRound', { number: row.roundNumber })
      : message(row.available ? 'liveSolo.draw.remaining' : 'liveSolo.draw.alreadyUsed') })))
  const directionCounts = computed(() => directionPoolCounts(state.value?.pool ?? []))
  const drawnRows = computed(() => poolRows.value.filter(x => x.roundNumber != null))
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
  const validPreset = computed(() => !!direction.value.trim() && (editingPreset.value ? !!questionGroupId.value : questionGroupIds.value.length > 0))

  async function load() {
    const generation = ++request, path = { competitionId: props.competitionId, matchId: props.match.id! }
    if (!path.matchId) return
    try {
      const response = await getLiveSoloDirectionPool({ path, query: { staff: props.staff } })
      if (disposed || generation !== request) return
      if (response.error || !response.data) throw parseLiveSoloError(response.error, message('liveSolo.error.load'))
      state.value = response.data
      if (props.staff && (response.data.canConfigure || response.data.canManagePresets)) {
        const [catalog, lists, directions] = await Promise.all([listLiveSoloDirectionPresets({ path }), listLiveSoloQuestionGroups({ path }),
          adminGetCompetitionDirections({ path: { competitionId: props.competitionId } })])
        if (disposed || generation !== request) return
        if (catalog.error || lists.error || directions.error) throw parseLiveSoloError(catalog.error ?? lists.error ?? directions.error, message('liveSolo.error.load'))
        presets.value = catalog.data?.items ?? []
        directionCatalog.value = (directions.data?.items ?? []).map(x => x.name ?? '').filter(Boolean)
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
    questionGroupId.value = preset?.questionGroupId ?? ''; questionGroupIds.value = []; groupSearch.value = ''; editorOpen.value = true
  }
  function toggleGroup(id: string, selected: boolean | 'indeterminate') {
    if (selected === true && !questionGroupIds.value.includes(id)) questionGroupIds.value = [...questionGroupIds.value, id]
    else if (selected !== true) questionGroupIds.value = questionGroupIds.value.filter(value => value !== id)
  }
  function selectVisibleGroups() { questionGroupIds.value = [...new Set([...questionGroupIds.value, ...batchGroups.value.map(x => x.value)])] }
  function clearGroups() { questionGroupIds.value = [] }
  function closeEditor() { editorOpen.value = false }
  async function savePreset() {
    if (!state.value?.canManagePresets || !validPreset.value) return
    if (editingPreset.value) {
      await mutate(() => saveLiveSoloDirectionPreset({ path: { competitionId: props.competitionId }, body: { id: presetId.value,
        name: presetName.value.trim(), direction: direction.value.trim(), questionGroupId: questionGroupId.value } }))
    } else {
      await mutate(async () => {
        const result = await saveLiveSoloDirectionPresets({ path: { competitionId: props.competitionId }, body: {
          direction: direction.value.trim(), questionGroupIds: [...questionGroupIds.value] } })
        if (result.data && state.value?.canConfigure) {
          poolEdited = true
          const items = result.data.items ?? [], boundGroups = new Set(items.map(x => x.questionGroupId))
          const retained = selectedIds.value.filter(id => !presets.value.some(x => x.id === id && boundGroups.has(x.questionGroupId)))
          selectedIds.value = [...new Set([...retained, ...items.map(x => x.id!).filter(Boolean)])]
        }
        return result
      })
    }
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
  watch([() => props.competitionId, () => props.match.id, () => props.staff,
    () => props.match.concurrencyStamp, () => props.round?.id, () => props.round?.state],
  ([competitionId, matchId, staff], previous) => {
    if (competitionId !== previous[0] || matchId !== previous[1] || staff !== previous[2]) {
      state.value = null; selectedIds.value = []; poolEdited = false; decisionReasons.value = {}; directionCatalog.value = []; questionGroupIds.value = []; closeEditor()
    }
    void load()
  }, { immediate: true })
  onScopeDispose(() => { disposed = true; request++ })
  return { state, presets: presetRows, groups, selectedCount, suggestedSlots, remaining, poolRows, directionCounts, drawnRows, busy, error, editorOpen, presetName, direction, questionGroupId,
    editingPreset, directionOptions, batchGroups, batchSelectedCount, groupSearch, toggleGroup, selectVisibleGroups, clearGroups,
    rescueUserId, rescueReason, decisionReasons, rescueOptions, requests, validPreset, togglePreset, editPreset, closeEditor, savePreset, removePreset, configure, draw, requestRescue, resolve }
}
export type LiveSoloMatchDirectionsState = import('vue').ShallowUnwrapRef<ReturnType<typeof useLiveSoloMatchDirections>>
