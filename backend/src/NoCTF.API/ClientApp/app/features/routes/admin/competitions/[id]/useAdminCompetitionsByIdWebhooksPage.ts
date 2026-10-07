import { message as describeMessage } from '../../../../../utils/i18n'
import { markRaw } from 'vue'
import StaffWebhooks from '../../../../competitions/staff-webhooks/StaffWebhooks.vue'
import { Copy, Plus, RotateCw, Send, Trash2, Webhook } from '@lucide/vue'
import { toast } from '../../../../../utils/message-toast'
import {
  adminCreateCompetitionWebhook,
  adminCreateCompetitionWebhookTestDelivery,
  adminDeleteCompetitionWebhook,
  adminGetCompetitionWebhookTestDelivery,
  adminListCompetitionWebhookDeliveries,
  adminListCompetitionWebhooks,
  adminRotateCompetitionWebhookSecret,
  adminUpdateCompetitionWebhook,
} from '../../../../../api'
import type {
  NoCtfapiEndpointsAdministrationCompetitionsCompetitionWebhookDeliveryDiagnosticResponse,
  NoCtfapiEndpointsAdministrationCompetitionsCompetitionWebhookTargetResponse,
} from '../../../../../api'
import { useOffsetPagination } from '../../../../../composables/useOffsetPagination'
import { useCompetitionAdmin } from '../../../../../lib/admin-competition'
import { adminFormatDateTime } from '../../../../../utils/admin-format'

interface WebhookForm {
  name: string
  endpointUrl: string
  enabled: boolean
}

/** Owns state, effects and commands for the competition webhook administration page. */
export function useAdminCompetitionsByIdWebhooksPage() {
  const { competitionId, canWrite } = useCompetitionAdmin()
  const canManage = ref(false)
  const formOpen = ref(false)
  const editingId = ref<string | null>(null)
  const form = reactive<WebhookForm>({ name: '', endpointUrl: '', enabled: false })
  const saving = ref(false)
  const pendingId = ref<string | null>(null)
  const deletingTarget = ref<NoCtfapiEndpointsAdministrationCompetitionsCompetitionWebhookTargetResponse | null>(null)
  const signingSecret = ref<string | null>(null)
  const secretOpen = ref(false)
  const testStates = reactive<Record<string, 'Pending' | 'Succeeded' | 'Failed'>>({})

  const mayManage = computed(() => canWrite.value && canManage.value)

  const pagination = useOffsetPagination<NoCtfapiEndpointsAdministrationCompetitionsCompetitionWebhookTargetResponse>(async ({ offset, limit, desc }) => {
    const { data, error: requestError } = await adminListCompetitionWebhooks({
      path: { competitionId },
      query: { offset, limit, desc },
    })
    if (requestError || !data)
      throw requestError ?? new Error(translate('webhook.loadFailed'))
    canManage.value = data.canManage ?? false
    return { items: data.items ?? [], total: data.total ?? 0 }
  }, { initialPageSize: 50, initialDesc: true })

  const targets = pagination.items
  const loading = pagination.loading
  const error = computed(() => pagination.error.value?.message ?? null)

  const deliveryPagination = useOffsetPagination<NoCtfapiEndpointsAdministrationCompetitionsCompetitionWebhookDeliveryDiagnosticResponse>(async ({ offset, limit, desc }) => {
    const { data, error: requestError } = await adminListCompetitionWebhookDeliveries({
      path: { competitionId },
      query: { offset, limit, desc },
    })
    if (requestError || !data)
      throw requestError ?? new Error(translate('webhook.diagnosticsLoadFailed'))
    return { items: data.items ?? [], total: data.total ?? 0 }
  }, { initialPageSize: 20, initialDesc: true })

  const deliveryStateKeys: Record<string, string> = {
    Pending: 'webhook.statePending',
    InFlight: 'webhook.stateInFlight',
    Delivered: 'webhook.stateDelivered',
    Suppressed: 'webhook.stateSuppressed',
    DeadLetter: 'webhook.stateDeadLetter',
  }
  const payloadStateKeys: Record<string, string> = {
    Unknown: 'webhook.payloadUnknown',
    Complete: 'webhook.payloadComplete',
    ProjectionNotReady: 'webhook.payloadProjectionNotReady',
    Invalid: 'webhook.payloadInvalid',
  }

  function deliveryStateLabel(state?: string) {
    return translate(deliveryStateKeys[state ?? ''] ?? 'webhook.statePending')
  }

  function payloadStateLabel(state?: string) {
    return translate(payloadStateKeys[state ?? ''] ?? 'webhook.payloadUnknown')
  }

  function deliveryStateVariant(state?: string): 'default' | 'secondary' | 'outline' | 'destructive' {
    if (state === 'Delivered') return 'default'
    if (state === 'DeadLetter') return 'destructive'
    if (state === 'InFlight') return 'outline'
    return 'secondary'
  }

  function formatSeconds(value?: number | null) {
    return value == null ? '—' : `${value.toFixed(2)}s`
  }

  async function refreshDeliveries() {
    await deliveryPagination.loadPage(deliveryPagination.page.value)
  }

  async function load() {
    await pagination.loadPage(pagination.page.value)
  }

  async function reloadFirstPage() {
    pagination.reset()
    await pagination.loadPage(1)
  }

  function createTarget() {
    editingId.value = null
    Object.assign(form, { name: '', endpointUrl: '', enabled: false })
    formOpen.value = true
  }

  function editTarget(target: NoCtfapiEndpointsAdministrationCompetitionsCompetitionWebhookTargetResponse) {
    if (!target.id || !target.endpointUrl) return
    editingId.value = target.id
    Object.assign(form, {
      name: target.name ?? '',
      endpointUrl: target.endpointUrl,
      enabled: target.enabled ?? false,
    })
    formOpen.value = true
  }

  function setFormOpen(open: boolean) {
    if (!saving.value) formOpen.value = open
  }

  async function save() {
    if (saving.value || !mayManage.value) return
    const name = form.name.trim()
    const endpointUrl = form.endpointUrl.trim()
    if (!name || !endpointUrl) {
      toast.error(describeMessage('webhook.completeRequiredFields'))
      return
    }
    saving.value = true
    const result = editingId.value
      ? await adminUpdateCompetitionWebhook({
          path: { competitionId, targetId: editingId.value },
          body: { name, endpointUrl, enabled: form.enabled },
        })
      : await adminCreateCompetitionWebhook({
          path: { competitionId },
          body: { name, endpointUrl, enabled: form.enabled },
        })
    saving.value = false
    if (result.error || !result.data) {
      toast.error(parseApiError(result.error, describeMessage('webhook.saveFailed')).displayMessage)
      return
    }
    if ('signingSecret' in result.data && result.data.signingSecret) {
      signingSecret.value = result.data.signingSecret
      secretOpen.value = true
    }
    formOpen.value = false
    toast.success(describeMessage('webhook.saved'))
    await reloadFirstPage()
  }

  async function setEnabled(
    target: NoCtfapiEndpointsAdministrationCompetitionsCompetitionWebhookTargetResponse,
    enabled: boolean,
  ) {
    if (!mayManage.value || !target.id || !target.endpointUrl) return
    pendingId.value = target.id
    const { error: requestError } = await adminUpdateCompetitionWebhook({
      path: { competitionId, targetId: target.id },
      body: { name: target.name ?? '', endpointUrl: target.endpointUrl, enabled },
    })
    pendingId.value = null
    if (requestError) {
      toast.error(parseApiError(requestError, describeMessage('webhook.saveFailed')).displayMessage)
      return
    }
    toast.success(enabled ? translate('webhook.enabled') : translate('webhook.disabled'))
    await reloadFirstPage()
  }

  function requestDelete(target: NoCtfapiEndpointsAdministrationCompetitionsCompetitionWebhookTargetResponse) {
    deletingTarget.value = target
  }

  function setDeleteOpen(open: boolean) {
    if (!open && !pendingId.value) deletingTarget.value = null
  }

  async function rotate(target: NoCtfapiEndpointsAdministrationCompetitionsCompetitionWebhookTargetResponse) {
    if (!mayManage.value || !target.id) return
    pendingId.value = target.id
    const { data, error: requestError } = await adminRotateCompetitionWebhookSecret({
      path: { competitionId, targetId: target.id },
    })
    pendingId.value = null
    if (requestError || !data?.signingSecret) {
      toast.error(parseApiError(requestError, describeMessage('webhook.rotateFailed')).displayMessage)
      return
    }
    signingSecret.value = data.signingSecret
    secretOpen.value = true
    toast.success(describeMessage('webhook.rotated'))
    await reloadFirstPage()
  }

  async function copySecret() {
    if (!signingSecret.value) return
    await navigator.clipboard.writeText(signingSecret.value)
    toast.success(describeMessage('webhook.secretCopied'))
  }

  async function remove() {
    const target = deletingTarget.value
    if (!target?.id || pendingId.value) return
    pendingId.value = target.id
    const { error: requestError } = await adminDeleteCompetitionWebhook({
      path: { competitionId, targetId: target.id },
    })
    pendingId.value = null
    if (requestError) {
      toast.error(parseApiError(requestError, describeMessage('webhook.deleteFailed')).displayMessage)
      return
    }
    deletingTarget.value = null
    toast.success(describeMessage('webhook.deleted'))
    await reloadFirstPage()
  }

  async function test(target: NoCtfapiEndpointsAdministrationCompetitionsCompetitionWebhookTargetResponse) {
    if (!mayManage.value || !target.id || testStates[target.id] === 'Pending') return
    testStates[target.id] = 'Pending'
    const { data, error: requestError } = await adminCreateCompetitionWebhookTestDelivery({
      path: { competitionId, targetId: target.id },
    })
    if (requestError || !data?.deliveryId) {
      testStates[target.id] = 'Failed'
      toast.error(parseApiError(requestError, describeMessage('webhook.testFailed')).displayMessage)
      return
    }
    for (let attempt = 0; attempt < 20; attempt++) {
      await new Promise(resolve => setTimeout(resolve, 750))
      const status = await adminGetCompetitionWebhookTestDelivery({
        path: { competitionId, targetId: target.id, deliveryId: data.deliveryId! },
      })
      if (status.data?.state && status.data.state !== 'Pending') {
        testStates[target.id] = status.data.state
        toast[status.data.state === 'Succeeded' ? 'success' : 'error'](
          status.data.state === 'Succeeded'
            ? translate('webhook.testSucceeded')
            : translate('webhook.testFailed'),
        )
        return
      }
    }
    testStates[target.id] = 'Failed'
    toast.error(describeMessage('webhook.testTimedOut'))
  }

  onMounted(() => {
    void load()
    void refreshDeliveries()
  })

  return {
    StaffWebhooks: markRaw(StaffWebhooks),
    Webhook,
    Plus,
    Copy,
    RotateCw,
    Send,
    Trash2,
    targets,
    mayManage,
    loading,
    error,
    page: pagination.page,
    pageCount: pagination.pageCount,
    total: pagination.total,
    pageLimit: pagination.limit,
    loadPage: pagination.loadPage,
    setPageSize: pagination.setPageSize,
    formOpen,
    editingId,
    form,
    saving,
    pendingId,
    deletingTarget,
    signingSecret,
    secretOpen,
    testStates,
    deliveries: deliveryPagination.items,
    deliveriesLoading: deliveryPagination.loading,
    deliveriesError: computed(() => deliveryPagination.error.value?.message ?? null),
    deliveriesPage: deliveryPagination.page,
    deliveriesPageCount: deliveryPagination.pageCount,
    deliveriesTotal: deliveryPagination.total,
    deliveriesPageLimit: deliveryPagination.limit,
    loadDeliveriesPage: deliveryPagination.loadPage,
    setDeliveriesPageSize: deliveryPagination.setPageSize,
    refreshDeliveries,
    deliveryStateLabel,
    deliveryStateVariant,
    payloadStateLabel,
    formatSeconds,
    adminFormatDateTime,
    load,
    createTarget,
    editTarget,
    setFormOpen,
    save,
    setEnabled,
    requestDelete,
    setDeleteOpen,
    rotate,
    copySecret,
    remove,
    test,
  }
}

export type AdminCompetitionsByIdWebhooksPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useAdminCompetitionsByIdWebhooksPage>>>
