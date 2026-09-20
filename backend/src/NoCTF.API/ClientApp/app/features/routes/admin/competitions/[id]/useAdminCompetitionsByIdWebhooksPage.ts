import { Copy, Plus, RotateCw, Send, Trash2, Webhook } from '@lucide/vue'
import { toast } from 'vue-sonner'
import {
  adminCreateCompetitionWebhook,
  adminCreateCompetitionWebhookTestDelivery,
  adminDeleteCompetitionWebhook,
  adminGetCompetitionWebhookTestDelivery,
  adminListCompetitionWebhooks,
  adminRotateCompetitionWebhookSecret,
  adminUpdateCompetitionWebhook,
} from '../../../../../api'
import type { NoCtfapiEndpointsAdministrationCompetitionsCompetitionWebhookTargetResponse } from '../../../../../api'
import { useCompetitionAdmin } from '../../../../../lib/admin-competition'

interface WebhookForm {
  name: string
  endpointUrl: string
  enabled: boolean
}

/** Owns state, effects and commands for the competition webhook administration page. */
export function useAdminCompetitionsByIdWebhooksPage() {
  const { competitionId, canWrite } = useCompetitionAdmin()
  const targets = ref<NoCtfapiEndpointsAdministrationCompetitionsCompetitionWebhookTargetResponse[]>([])
  const canManage = ref(false)
  const nextCursor = ref<string | null>(null)
  const loading = ref(true)
  const loadingMore = ref(false)
  const error = ref<string | null>(null)
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

  async function load(reset = true) {
    if (reset) loading.value = true
    else loadingMore.value = true
    const { data, error: requestError } = await adminListCompetitionWebhooks({
      path: { competitionId },
      query: { limit: 50, cursor: reset ? undefined : nextCursor.value ?? undefined },
    })
    loading.value = false
    loadingMore.value = false
    if (requestError || !data) {
      error.value = parseApiError(requestError, translate('webhook.loadFailed')).message
      return
    }
    targets.value = reset ? data.items ?? [] : [...targets.value, ...(data.items ?? [])]
    canManage.value = data.canManage ?? false
    nextCursor.value = data.nextCursor ?? null
    error.value = null
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
      toast.error(translate('webhook.completeRequiredFields'))
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
      toast.error(parseApiError(result.error, translate('webhook.saveFailed')).message)
      return
    }
    if ('signingSecret' in result.data && result.data.signingSecret) {
      signingSecret.value = result.data.signingSecret
      secretOpen.value = true
    }
    formOpen.value = false
    toast.success(translate('webhook.saved'))
    await load()
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
      toast.error(parseApiError(requestError, translate('webhook.saveFailed')).message)
      return
    }
    toast.success(enabled ? translate('webhook.enabled') : translate('webhook.disabled'))
    await load()
  }

  function requestDelete(target: NoCtfapiEndpointsAdministrationCompetitionsCompetitionWebhookTargetResponse) {
    deletingTarget.value = target
  }

  function setDeleteOpen(open: boolean) {
    if (!open && !pendingId.value) deletingTarget.value = null
  }

  function loadMore() {
    return load(false)
  }

  async function rotate(target: NoCtfapiEndpointsAdministrationCompetitionsCompetitionWebhookTargetResponse) {
    if (!mayManage.value || !target.id) return
    pendingId.value = target.id
    const { data, error: requestError } = await adminRotateCompetitionWebhookSecret({
      path: { competitionId, targetId: target.id },
    })
    pendingId.value = null
    if (requestError || !data?.signingSecret) {
      toast.error(parseApiError(requestError, translate('webhook.rotateFailed')).message)
      return
    }
    signingSecret.value = data.signingSecret
    secretOpen.value = true
    toast.success(translate('webhook.rotated'))
    await load()
  }

  async function copySecret() {
    if (!signingSecret.value) return
    await navigator.clipboard.writeText(signingSecret.value)
    toast.success(translate('webhook.secretCopied'))
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
      toast.error(parseApiError(requestError, translate('webhook.deleteFailed')).message)
      return
    }
    deletingTarget.value = null
    toast.success(translate('webhook.deleted'))
    await load()
  }

  async function test(target: NoCtfapiEndpointsAdministrationCompetitionsCompetitionWebhookTargetResponse) {
    if (!mayManage.value || !target.id || testStates[target.id] === 'Pending') return
    testStates[target.id] = 'Pending'
    const { data, error: requestError } = await adminCreateCompetitionWebhookTestDelivery({
      path: { competitionId, targetId: target.id },
    })
    if (requestError || !data?.deliveryId) {
      testStates[target.id] = 'Failed'
      toast.error(parseApiError(requestError, translate('webhook.testFailed')).message)
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
    toast.error(translate('webhook.testTimedOut'))
  }

  onMounted(() => load())

  return {
    Webhook,
    Plus,
    Copy,
    RotateCw,
    Send,
    Trash2,
    targets,
    mayManage,
    nextCursor,
    loading,
    loadingMore,
    error,
    formOpen,
    editingId,
    form,
    saving,
    pendingId,
    deletingTarget,
    signingSecret,
    secretOpen,
    testStates,
    load,
    createTarget,
    editTarget,
    setFormOpen,
    save,
    setEnabled,
    requestDelete,
    setDeleteOpen,
    loadMore,
    rotate,
    copySecret,
    remove,
    test,
  }
}

export type AdminCompetitionsByIdWebhooksPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useAdminCompetitionsByIdWebhooksPage>>>
