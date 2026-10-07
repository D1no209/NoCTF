import { adminCreateStaffWebhook, adminListStaffWebhooks, adminUpdateStaffWebhook, adminDeleteStaffWebhook,
  adminRotateStaffWebhookSecret, adminTestStaffWebhook, adminGetStaffWebhookTest, adminListStaffWebhookDeliveries } from '../../../api'
import type { NoCtfapiEndpointsAdministrationStaffWebhooksStaffWebhookTargetResponse as Target,
  NoCtfapiEndpointsAdministrationStaffWebhooksStaffWorkItemKindProtocol as Category,
  NoCtfApplicationCompetitionsStaffWebhooksStaffWebhookDeliveryView as Delivery,
  NoCtfDomainCompetitionsStaffWebhooksStaffWebhookDeliveryState as DeliveryState } from '../../../api'
import { useCompetitionAdmin } from '../../../lib/admin-competition'
import { useOffsetPagination } from '../../../composables/useOffsetPagination'
import { usePolling } from '../../../composables/usePolling'
import { message } from '../../../utils/i18n'
import type { MessageKey } from '../../../locales/en'
import { toast } from '../../../utils/message-toast'

export function useStaffWebhooks() {
  const { competitionId, canWrite } = useCompetitionAdmin()
  const canManage = ref(false)
  const mayManage = computed(() => canWrite.value && canManage.value)
  const formOpen = ref(false)
  const editingId = ref<string | null>(null)
  const form = reactive({ name: '', endpointUrl: '', enabled: false, categories: ['CheatIncident', 'Consultation', 'BanAppeal'] as Category[] })
  const categories: Category[] = ['CheatIncident', 'Consultation', 'BanAppeal']
  const categoryKeys: Record<Category, MessageKey> = { CheatIncident: 'staffWebhook.cheatIncident', Consultation: 'staffWebhook.consultation', BanAppeal: 'staffWebhook.banAppeal' }
  const stateKeys: Record<DeliveryState, MessageKey> = { 0: 'webhook.statePending', 1: 'webhook.stateInFlight', 2: 'webhook.stateDelivered', 3: 'webhook.stateSuppressed', 4: 'webhook.stateDeadLetter' }
  const pending = ref(false)
  const signingSecret = ref<string | null>(null)
  const secretOpen = computed(() => signingSecret.value !== null)
  const deleting = shallowRef<Target | null>(null)
  const recordsTarget = shallowRef<Target | null>(null)
  const activeTest = shallowRef<{ targetId: string; deliveryId: string } | null>(null)
  const testState = ref<DeliveryState | null>(null)
  const page = useOffsetPagination<Target>(async ({ offset, limit, desc }) => {
    const result = await adminListStaffWebhooks({ path: { competitionId }, query: { offset, limit, desc } })
    if (result.error || !result.data) throw result.error
    canManage.value = Boolean(result.data.canManage)
    return { items: result.data.items ?? [], total: result.data.total ?? 0 }
  }, { initialPageSize: 20 })
  const records = useOffsetPagination<Delivery>(async ({ offset, limit, desc }) => {
    if (!recordsTarget.value?.id) return { items: [], total: 0 }
    const result = await adminListStaffWebhookDeliveries({ path: { competitionId, targetId: recordsTarget.value.id }, query: { offset, limit, desc } })
    if (result.error || !result.data) throw result.error
    return { items: result.data.items ?? [], total: result.data.total ?? 0 }
  }, { initialPageSize: 20 })
  const poll = usePolling(async () => {
    const current = activeTest.value; if (!current) return true
    const result = await adminGetStaffWebhookTest({ path: { competitionId, ...current } })
    if (result.error || !result.data) throw result.error
    testState.value = result.data.state ?? 0
    return testState.value >= 2
  }, { timeout: 60_000 })
  function create() { editingId.value = null; Object.assign(form, { name: '', endpointUrl: '', enabled: false, categories: [...categories] }); formOpen.value = true }
  function edit(value: Target) { if (!value.id || !value.endpointUrl) return; editingId.value = value.id;
    Object.assign(form, { name: value.name ?? '', endpointUrl: value.endpointUrl, enabled: Boolean(value.enabled), categories: [...value.categories ?? []] }); formOpen.value = true }
  function setFormOpen(open: boolean) { if (!pending.value) formOpen.value = open }
  function toggleCategory(category: Category, checked: boolean | 'indeterminate') {
    form.categories = checked === true ? [...new Set([...form.categories, category])] : form.categories.filter(value => value !== category)
  }
  function setSecretOpen(open: boolean) { if (!open) signingSecret.value = null }
  function requestDelete(value: Target) { deleting.value = value }
  function setDeleteOpen(open: boolean) { if (!open && !pending.value) deleting.value = null }
  async function reload() { await page.loadPage(page.page.value) }
  async function save() {
    if (!mayManage.value || pending.value || !form.categories.length) return
    pending.value = true
    try {
      const body = { name: form.name.trim(), endpointUrl: form.endpointUrl.trim(), enabled: form.enabled, categories: [...form.categories] }
      const result = editingId.value
        ? await adminUpdateStaffWebhook({ path: { competitionId, targetId: editingId.value }, body })
        : await adminCreateStaffWebhook({ path: { competitionId }, body })
      if (result.error || !result.data) throw result.error
      signingSecret.value = result.data.signingSecret ?? null; formOpen.value = false; toast.success(message('staffWebhook.saved')); await reload()
    } catch (error) { toast.error(parseApiError(error).displayMessage) }
    finally { pending.value = false }
  }
  async function rotate(value: Target) {
    if (!mayManage.value || !value.id || pending.value) return
    pending.value = true
    try { const result = await adminRotateStaffWebhookSecret({ path: { competitionId, targetId: value.id } });
      if (result.error || !result.data) throw result.error; signingSecret.value = result.data.signingSecret ?? null; await reload() }
    catch (error) { toast.error(parseApiError(error).displayMessage) } finally { pending.value = false }
  }
  async function remove() {
    const id = deleting.value?.id; if (!mayManage.value || !id || pending.value) return
    pending.value = true
    try { const result = await adminDeleteStaffWebhook({ path: { competitionId, targetId: id } });
      if (result.error) throw result.error; deleting.value = null; toast.success(message('staffWebhook.deleted')); await reload() }
    catch (error) { toast.error(parseApiError(error).displayMessage) } finally { pending.value = false }
  }
  async function test(value: Target) {
    if (!mayManage.value || !value.id || pending.value) return
    pending.value = true; poll.stop()
    try { const result = await adminTestStaffWebhook({ path: { competitionId, targetId: value.id } });
      if (result.error || !result.data?.deliveryId) throw result.error
      activeTest.value = { targetId: value.id, deliveryId: result.data.deliveryId }; testState.value = 0; poll.start() }
    catch (error) { toast.error(parseApiError(error).displayMessage) } finally { pending.value = false }
  }
  async function showRecords(value: Target) { recordsTarget.value = value; records.reset(); await records.loadPage(1) }
  async function copySecret() { if (signingSecret.value) { await navigator.clipboard.writeText(signingSecret.value); toast.success(message('webhook.secretCopied')) } }
  onMounted(() => void reload())
  onBeforeUnmount(() => { poll.stop(); signingSecret.value = null })
  return { page, records, categories, categoryKeys, stateKeys, mayManage, formOpen, editingId, form, pending, signingSecret, secretOpen,
    deleting, recordsTarget, testState, poll, create, edit, setFormOpen, toggleCategory, setSecretOpen, requestDelete, setDeleteOpen,
    reload, save, rotate, remove, test, showRecords, copySecret }
}
export type StaffWebhooksViewState = import('vue').ShallowUnwrapRef<ReturnType<typeof useStaffWebhooks>>
