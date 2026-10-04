
import { api, RequestPolicyOption } from '../../../../../lib/api'
import { message as describeMessage } from '../../../../../utils/i18n'
import type { UiMessage } from '../../../../../utils/i18n'
import { Plus, Trash2, RefreshCw } from '@lucide/vue'
import { toast } from '../../../../../utils/message-toast'

import type { NoCTFAPIEndpointsAdministrationCompetitionsCompetitionDirectionResponse as Direction } from '~/api/models'
import { useCompetitionAdmin } from '~/lib/admin-competition'
import { isLucideIconName, normalizeLucideIconName } from '~/lib/lucide-icon-name'

export function validateCompetitionDirections(items: ReadonlyArray<Direction>): boolean {
  const names = items.map(item => item.name?.trim().toLowerCase())
  return items.length > 0 && items.length <= 64 && names.every(name => !!name && name.length <= 96)
    && new Set(names).size === names.length && items.every(item => isLucideIconName(item.icon ?? ''))
}

export function useAdminCompetitionsByIdDirectionsPage() {
  const { competitionId, canWrite } = useCompetitionAdmin()
  const items = ref<Array<Direction & { name: string; icon: string }>>([])
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
    const settledRequests = await Promise.allSettled([
      api.api.v1.admin.competitions.byCompetitionId(competitionId).directions.get({ options: [new RequestPolicyOption({ signal: request.signal })] }),
      api.api.v1.admin.competitions.byCompetitionId(competitionId).challenges.get({ queryParameters: { includeDeleted: true }, options: [new RequestPolicyOption({ signal: request.signal })] }),
    ]);
    const catalog = settledRequests[0].status === 'fulfilled' ? settledRequests[0].value : undefined;
    const catalogError = settledRequests[0].status === 'rejected' ? settledRequests[0].reason : undefined;
    const challenges = settledRequests[1].status === 'fulfilled' ? settledRequests[1].value : undefined;
    const challengesError = settledRequests[1].status === 'rejected' ? settledRequests[1].reason : undefined;

    if (version !== generation) return
    loading.value = false
    if (catalogError || !catalog || challengesError) {
      error.value = parseApiError(catalogError ?? challengesError).displayMessage
      return
    }
    items.value = (catalog.items ?? []).map(item => ({ ...item, name: item.name ?? '', icon: item.icon ?? '' }))
    used.value = new Set((challenges?.items ?? []).flatMap(item => item.directionId ? [item.directionId] : []))
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
      let resultError: unknown;
      const result = await api.api.v1.admin.competitions.byCompetitionId(competitionId).directions.put({ items: items.value.map(item => ({ ...item, name: item.name?.trim(), icon: normalizeLucideIconName(item.icon ?? '') })) }).catch(cause => { resultError = cause; return undefined });
      if (version !== generation) return
      if (resultError || !result) {
        const code = resultError && typeof resultError === 'object' && 'code' in resultError ? resultError.code : null
        error.value = code ? translate(`directionSettings.errors.${code}`) : parseApiError(resultError).displayMessage
        return
      }
      items.value = (result.items ?? []).map(item => ({ ...item, name: item.name ?? '', icon: item.icon ?? '' }))
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
