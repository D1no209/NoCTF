import { Plus, Pencil, Trash2 } from '@lucide/vue'
import { markRaw } from 'vue'
import { adminListCompetitionChallenges, adminGetCompetitionScorePresets, adminCreateCompetitionScorePreset, adminUpdateCompetitionScorePreset, adminDeleteCompetitionScorePreset } from '~/api'
import { competitionTagCatalog } from '~/lib/challenge-tags'
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
  const presets = ref<Preset[]>([])
  const challenges = ref<Array<{ tags?: string[] }>>([])
  const items = computed(() => competitionTagCatalog(presets.value, challenges.value))
  const loading = ref(true)
  const saving = ref(false)
  const deleting = ref<string | null>(null)
  const error = ref<import('~/utils/i18n').UiMessage | null>(null)
  const editing = ref(false)
  const editId = ref<string | null>(null)
  const name = ref('')
  const hasScoreCurve = ref(false)
  const curve = ref<PointsCurveValue>({ initialPoints: 500, minimumPoints: 100, decayTeamCount: 10, decayMode: 'Quadratic', customExpression: null })
  const field = computed<ConfigFieldDef>(() => ({ key: 'scoreCurve', label: translate('common.label.scoreCurve'), type: 'pointsCurve' }))
  const canSave = computed(() => {
    if (!canWrite.value || saving.value || !name.value.trim() || name.value.trim().length > 40) return false
    if (!hasScoreCurve.value) return true
    return Number.isInteger(curve.value.initialPoints) && (curve.value.initialPoints ?? 0) > 0 && (curve.value.initialPoints ?? 0) <= 1_000_000
      && Number.isInteger(curve.value.minimumPoints) && (curve.value.minimumPoints ?? -1) >= 0 && (curve.value.minimumPoints ?? 0) <= (curve.value.initialPoints ?? 0)
      && Number.isInteger(curve.value.decayTeamCount) && (curve.value.decayTeamCount ?? 0) > 1
      && (curve.value.decayMode !== 'Custom' || !!curve.value.customExpression?.trim())
  })
  const request = new AbortController()
  async function load() {
    loading.value = true
    const [result, allChallenges] = await Promise.all([
      adminGetCompetitionScorePresets({ path: { competitionId }, signal: request.signal }),
      adminListCompetitionChallenges({ path: { competitionId }, query: { includeDeleted: true }, signal: request.signal }),
    ])
    if (request.signal.aborted) return
    loading.value = false
    const failure = result.error ?? allChallenges.error
    error.value = failure ? parseApiError(failure).displayMessage : null
    if (!failure && result.data && allChallenges.data) {
      presets.value = result.data.items ?? []
      challenges.value = allChallenges.data.items ?? []
    }
  }
  function start(item?: Preset) {
    editId.value = item?.id ?? null
    name.value = item?.name ?? ''
    hasScoreCurve.value = !!item?.scoreCurve
    curve.value = item?.scoreCurve ? { initialPoints: item.scoreCurve.initialPoints ?? null,
      minimumPoints: item.scoreCurve.minimumPoints ?? null, decayTeamCount: item.scoreCurve.decayTeamCount ?? null,
      decayMode: item.scoreCurve.decayMode ?? 'Quadratic', customExpression: item.scoreCurve.customExpression ?? null }
      : { initialPoints: 500, minimumPoints: 100, decayTeamCount: 10, decayMode: 'Quadratic', customExpression: null }
    editing.value = true
  }
  function create() { start() }
  function close() { editing.value = false }
  function setEditing(open: boolean) { if (!saving.value) editing.value = open }
  function editTag(item: { name: string; preset?: Preset }) {
    start(item.preset)
    name.value = item.name
  }
  function updateCurve(value: unknown) { curve.value = value as PointsCurveValue }
  function modeLabel(item: Preset) { return SCORE_DECAY_MODES.find(mode => mode.value === item.scoreCurve?.decayMode)?.label ?? 'common.label.scoreCurve' }
  async function save() {
    if (!canSave.value) return
    saving.value = true
    try {
      const body = { name: name.value.trim(), scoreCurve: hasScoreCurve.value ? curve.value as Curve : null }
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
    error, editing, editId, name, hasScoreCurve, curve, field, canSave, canWrite, load, editTag, create, close, setEditing, updateCurve, modeLabel, save, remove }
}
export type AdminCompetitionsByIdScorePresetsPageViewState = import('vue').ShallowUnwrapRef<ReturnType<typeof useAdminCompetitionsByIdScorePresetsPage>>
