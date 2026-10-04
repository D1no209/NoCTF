import { message as describeMessage } from '../../../../../utils/i18n'
import type { UiMessage } from '../../../../../utils/i18n'
import { Plus, Trash2, RefreshCw } from '@lucide/vue'
import { toast } from '../../../../../utils/message-toast'
import { adminGetCompetitionDirections, adminSaveCompetitionDirections, adminListCompetitionChallenges } from '~/api'
import type { NoCtfapiEndpointsAdministrationCompetitionsCompetitionDirectionResponse as Direction } from '~/api'
import { useCompetitionAdmin } from '~/lib/admin-competition'
import { isLucideIconName, normalizeLucideIconName } from '~/lib/lucide-icon-name'

export function validateCompetitionDirections(items: ReadonlyArray<Direction>): boolean {
  const names = items.map(item => item.name?.trim().toLowerCase())
  return items.length > 0 && items.length <= 64 && names.every(name => !!name && name.length <= 96)
    && new Set(names).size === names.length && items.every(item => isLucideIconName(item.icon ?? ''))
}

export function useAdminCompetitionsByIdDirectionsPage() {
  const { competitionId, canWrite } = useCompetitionAdmin()
  const items = ref<Direction[]>([])
  const loading = ref(true)
  const saving = ref(false)
  const error = ref<UiMessage | null>(null)
  const used = ref(new Set<string>())
  let generation = 0
  let request: AbortController | undefined
  const canSave = computed(() => canWrite.value && !loading.value && !saving.value && validateCompetitionDirections(items.value))
  async function load() {
    const version = ++generation
    request?.abort()
    request = new AbortController()
    loading.value = true
    error.value = null
    const [catalog, challenges] = await Promise.all([
      adminGetCompetitionDirections({ path: { competitionId }, signal: request.signal }),
      adminListCompetitionChallenges({ path: { competitionId }, query: { includeDeleted: true }, signal: request.signal }),
    ])
    if (version !== generation) return
    loading.value = false
    if (catalog.error || !catalog.data || challenges.error) {
      error.value = parseApiError(catalog.error ?? challenges.error).displayMessage
      return
    }
    items.value = catalog.data.items ?? []
    used.value = new Set((challenges.data?.items ?? []).flatMap(item => item.directionId ? [item.directionId] : []))
  }
  function add() { items.value.push({ id: crypto.randomUUID(), name: '', icon: 'flag' }) }
  function remove(item: Direction) {
    if (!canWrite.value || saving.value || items.value.length <= 1 || used.value.has(item.id ?? '')) return
    items.value = items.value.filter(value => value.id !== item.id)
  }
  async function save() {
    if (!canSave.value) return
    saving.value = true
    error.value = null
    const version = generation
    try {
      const result = await adminSaveCompetitionDirections({ path: { competitionId },
        body: { items: items.value.map(item => ({ ...item, name: item.name?.trim(), icon: normalizeLucideIconName(item.icon ?? '') })) } })
      if (version !== generation) return
      if (result.error || !result.data) {
        const code = result.error && typeof result.error === 'object' && 'code' in result.error ? result.error.code : null
        error.value = code ? translate(`directionSettings.errors.${code}`) : parseApiError(result.error).displayMessage
        return
      }
      items.value = result.data.items ?? []
      toast.success(describeMessage('directionSettings.saved'))
    }
    catch (failure) { if (version === generation) error.value = parseApiError(failure).displayMessage }
    finally { if (version === generation) saving.value = false }
  }
  function canRemove(item: Direction) { return items.value.length > 1 && !used.value.has(item.id ?? '') }
  onMounted(() => void load())
  onBeforeUnmount(() => { generation++; request?.abort() })
  return { Plus, Trash2, RefreshCw, items, loading, saving, error, canWrite, canSave, load, add, remove, save, canRemove, isLucideIconName }
}
export type AdminCompetitionsByIdDirectionsPageViewState = import('vue').ShallowUnwrapRef<ReturnType<typeof useAdminCompetitionsByIdDirectionsPage>>
