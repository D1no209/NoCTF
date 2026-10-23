import { Plus, Pencil, Trash2 } from '@lucide/vue'
import { markRaw } from 'vue'
import { adminGetCompetitionScorePresets, adminCreateCompetitionScorePreset, adminUpdateCompetitionScorePreset, adminDeleteCompetitionScorePreset } from '~/api'
import type { NoCtfapiEndpointsAdministrationCompetitionsCompetitionScorePresetResponse as Preset,
  NoCtfapiEndpointsAdministrationCompetitionsScoreCurveContract as Curve } from '~/api'
import { useCompetitionAdmin } from '~/lib/admin-competition'
import type { ConfigFieldDef, PointsCurveValue } from '~/utils/game-config'
import { SCORE_DECAY_MODES } from '~/utils/game-config'
import { message } from '~/utils/i18n'
import { toast } from '~/utils/message-toast'
import ConfigFieldInputComponent from '~/features/admin/ConfigFieldInput.vue'

export function useAdminCompetitionsByIdScorePresetsPage() {
  const { competitionId, canWrite } = useCompetitionAdmin()
  const items = ref<Preset[]>([])
  const loading = ref(true)
  const saving = ref(false)
  const deleting = ref<string | null>(null)
  const error = ref<import('~/utils/i18n').UiMessage | null>(null)
  const editing = ref(false)
  const editId = ref<string | null>(null)
  const name = ref('')
  const curve = ref<PointsCurveValue>({ initialPoints: 500, minimumPoints: 100, decayTeamCount: 10, decayMode: 'Quadratic', customExpression: null })
  const field = computed<ConfigFieldDef>(() => ({ key: 'scoreCurve', label: translate('common.label.scoreCurve'), type: 'pointsCurve' }))
  const canSave = computed(() => canWrite.value && !saving.value && !!name.value.trim() && name.value.trim().length <= 40
    && Number.isInteger(curve.value.initialPoints) && (curve.value.initialPoints ?? 0) > 0 && (curve.value.initialPoints ?? 0) <= 1_000_000
    && Number.isInteger(curve.value.minimumPoints) && (curve.value.minimumPoints ?? -1) >= 0 && (curve.value.minimumPoints ?? 0) <= (curve.value.initialPoints ?? 0)
    && Number.isInteger(curve.value.decayTeamCount) && (curve.value.decayTeamCount ?? 0) > 1
    && (curve.value.decayMode !== 'Custom' || !!curve.value.customExpression?.trim()))
  const request = new AbortController()
  async function load() {
    loading.value = true
    const result = await adminGetCompetitionScorePresets({ path: { competitionId }, signal: request.signal })
    if (request.signal.aborted) return
    loading.value = false
    error.value = result.error ? parseApiError(result.error).displayMessage : null
    if (result.data) items.value = result.data.items ?? []
  }
  function start(item?: Preset) {
    editId.value = item?.id ?? null
    name.value = item?.name ?? ''
    curve.value = item?.scoreCurve ? { initialPoints: item.scoreCurve.initialPoints ?? null,
      minimumPoints: item.scoreCurve.minimumPoints ?? null, decayTeamCount: item.scoreCurve.decayTeamCount ?? null,
      decayMode: item.scoreCurve.decayMode ?? 'Quadratic', customExpression: item.scoreCurve.customExpression ?? null }
      : { initialPoints: 500, minimumPoints: 100, decayTeamCount: 10, decayMode: 'Quadratic', customExpression: null }
    editing.value = true
  }
  function create() { start() }
  function close() { editing.value = false }
  function updateCurve(value: unknown) { curve.value = value as PointsCurveValue }
  function modeLabel(item: Preset) { return SCORE_DECAY_MODES.find(mode => mode.value === item.scoreCurve?.decayMode)?.label ?? 'common.label.scoreCurve' }
  async function save() {
    if (!canSave.value) return
    saving.value = true
    try {
      const body = { name: name.value.trim(), scoreCurve: curve.value as Curve }
      const result = editId.value
        ? await adminUpdateCompetitionScorePreset({ path: { competitionId, presetId: editId.value }, body })
        : await adminCreateCompetitionScorePreset({ path: { competitionId }, body })
      if (result.error) { toast.error(parseApiError(result.error).displayMessage); return }
      editing.value = false
      toast.success(message('scorePresets.saved', { affected: result.data?.affectedChallengeCount ?? 0, replaced: result.data?.replacedChallengeCount ?? 0 }))
      await load()
    } finally { saving.value = false }
  }
  async function remove(item: Preset) {
    if (!canWrite.value || !item.id || deleting.value) return
    deleting.value = item.id
    try {
      const result = await adminDeleteCompetitionScorePreset({ path: { competitionId, presetId: item.id } })
      if (result.error) { toast.error(parseApiError(result.error).displayMessage); return }
      if (editId.value === item.id) editing.value = false
      toast.success(message('scorePresets.deleted', { affected: result.data?.affectedChallengeCount ?? 0 }))
      await load()
    } finally { deleting.value = null }
  }
  onMounted(() => void load())
  onBeforeUnmount(() => request.abort())
  return { Plus, Pencil, Trash2, ConfigFieldInput: markRaw(ConfigFieldInputComponent), items, loading, saving, deleting,
    error, editing, editId, name, curve, field, canSave, canWrite, load, start, create, close, updateCurve, modeLabel, save, remove }
}
export type AdminCompetitionsByIdScorePresetsPageViewState = import('vue').ShallowUnwrapRef<ReturnType<typeof useAdminCompetitionsByIdScorePresetsPage>>
