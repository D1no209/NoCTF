import { message as describeMessage } from '../../utils/i18n'
import { computed, onScopeDispose, ref, watch } from 'vue'
import { toast } from '../../utils/message-toast'
import { adminListRuntimeFlags } from '~/api'
import type { NoCtfapiEndpointsAdministrationRuntimeRuntimeFlagResponse } from '~/api'
import { useOffsetPagination } from '~/composables/useOffsetPagination'
import { adminFormatDateTime } from '~/utils/admin-format'
import { parseApiError } from '~/utils/api-error'

type RuntimeFlag = NoCtfapiEndpointsAdministrationRuntimeRuntimeFlagResponse

export function useRuntimeFlagsPanel(props: Readonly<{ runtimeId: string; autoLoad: boolean }>) {
  const queried = ref(false)
  const includeHistory = ref(false)
  const revealed = ref(new Set<string>())
  const pagination = useOffsetPagination<RuntimeFlag>(async ({ offset, limit }) => {
    const { data, error } = await adminListRuntimeFlags({ path: { runtimeInstanceId: props.runtimeId },
      query: { includeHistory: includeHistory.value, offset, limit, desc: true } })
    if (error || !data) throw parseApiError(error, describeMessage('runtimeFlags.failed'))
    return { items: data.items ?? [], total: data.total ?? 0 }
  })
  function load() {
    queried.value = true
    revealed.value = new Set()
    return pagination.loadPage()
  }
  watch(() => props.runtimeId, () => {
    pagination.reset()
    queried.value = false
    revealed.value = new Set()
  }, { flush: 'sync' })
  watch([() => props.runtimeId, () => props.autoLoad], () => {
    if (props.autoLoad) void load()
  }, { immediate: true })
  watch(includeHistory, () => {
    pagination.reset()
    revealed.value = new Set()
    if (queried.value) void load()
  })
  watch(pagination.page, () => { revealed.value = new Set() })
  onScopeDispose(() => { pagination.reset(); revealed.value = new Set() })

  const rows = computed(() => pagination.items.value.map((item) => ({
    ...item,
    revealed: Boolean(item.flag?.id && revealed.value.has(item.flag.id)),
    sourceKey: `runtimeFlags.source.${item.source ?? 'Static'}`,
    stateKey: `runtimeFlags.state.${item.state ?? 'Active'}`,
    matchKey: item.flag?.matchKind === 'RegularExpression' ? 'runtimeFlags.regex' : 'runtimeFlags.exact',
    round: item.source === 'AwdRound' ? Number.parseInt(item.flag?.specificationId?.slice(0, 8) ?? '', 10) : null,
  })))
  function toggle(item: RuntimeFlag) {
    const id = item.flag?.id
    if (!id) return
    const next = new Set(revealed.value)
    if (next.has(id)) next.delete(id)
    else next.add(id)
    revealed.value = next
  }
  async function copy(item: RuntimeFlag) {
    if (!item.flag?.flag) return
    try {
      await navigator.clipboard.writeText(item.flag.flag)
      toast.success(describeMessage('common.label.copiedClipboard'))
    }
    catch { toast.error(describeMessage('common.kohPanel.error.copyManuallySelectFailed')) }
  }
  return { queried, includeHistory, rows, load, toggle, copy, adminFormatDateTime,
    loading: pagination.loading, error: computed(() => pagination.error.value?.message ?? null),
    page: pagination.page, pageCount: pagination.pageCount, total: pagination.total,
    pageLimit: pagination.limit, loadPage: pagination.loadPage, setPageSize: pagination.setPageSize }
}
export type RuntimeFlagsPanelViewState = import('vue').ShallowUnwrapRef<ReturnType<typeof useRuntimeFlagsPanel>>
