
import { api } from '../../../../../lib/api'
import { message as describeMessage } from '../../../../../utils/i18n'
import type { UiMessage } from '../../../../../utils/i18n'
import { adminUserPath } from '~/features/admin/admin-navigation'
import { proxyRefs } from 'vue'

import { X } from '@lucide/vue'
import { toast } from '../../../../../utils/message-toast'

import type { NoCTFAPIEndpointsAdministrationCompetitionsCompetitionPermissionCandidateResponse, NoCTFAPIEndpointsAdministrationCompetitionsCompetitionPermissionsResponse } from '../../../../../api/models'
import { useCompetitionAdmin } from '../../../../../lib/admin-competition'

/** Owns state, effects and commands for AdminCompetitionsByIdPermissionsPage. */
export function useAdminCompetitionsByIdPermissionsPage() {
  const { competitionId, canManagePermissions, refresh } = useCompetitionAdmin()

  const permissions = ref<NoCTFAPIEndpointsAdministrationCompetitionsCompetitionPermissionsResponse | null>(null)

  const candidates = ref<NoCTFAPIEndpointsAdministrationCompetitionsCompetitionPermissionCandidateResponse[]>([])

  const loading = ref(true)

  const error = ref<UiMessage | null>(null)

  const managerIds = ref<string[]>([])

  const judgeIds = ref<string[]>([])

  const observerIds = ref<string[]>([])

  const { user } = useAuth()

  const candidateName = (id: string) =>
    candidates.value.find(c => c.id === id)?.userName
    ?? (id && id === user.value?.userId ? user.value?.userName : undefined)
    ?? id

  async function load() {
    loading.value = true
    error.value = null
    const settledRequests = await Promise.allSettled([
      api.api.v1.admin.competitions.byCompetitionId(competitionId).get(),
      api.api.v1.admin.competitions.byCompetitionId(competitionId).permissionCandidates.get(),
    ]);
    const perm = settledRequests[0].status === 'fulfilled' ? settledRequests[0].value : undefined;
    const permError = settledRequests[0].status === 'rejected' ? settledRequests[0].reason : undefined;
    const cand = settledRequests[1].status === 'fulfilled' ? settledRequests[1].value : undefined;

    if (permError) {
      error.value = parseApiError(permError).displayMessage
      loading.value = false
      return
    }
    permissions.value = perm?.permissions ?? null
    candidates.value = cand?.items ?? []
    managerIds.value = [...(perm?.permissions?.managerIds ?? [])]
    judgeIds.value = [...(perm?.permissions?.judgeIds ?? [])]
    observerIds.value = [...(perm?.permissions?.observerIds ?? [])]
    loading.value = false
  }

  const search = ref('')

  const filteredCandidates = computed(() => {
    const q = search.value.trim().toLowerCase()
    const list = candidates.value.filter(c => c.id && c.id !== permissions.value?.ownerId)
    if (!q) return list.slice(0, 20)
    return list.filter(c => c.userName?.toLowerCase().includes(q)).slice(0, 20)
  })

  function assigned(id?: string | null): boolean {
    if (!id) return true
    return managerIds.value.includes(id) || judgeIds.value.includes(id) || observerIds.value.includes(id)
  }

  function add(role: 'manager' | 'judge' | 'observer', id?: string | null) {
    if (!id || assigned(id)) return
    if (role === 'manager') managerIds.value.push(id)
    else if (role === 'judge') judgeIds.value.push(id)
    else observerIds.value.push(id)
  }

  function remove(role: 'manager' | 'judge' | 'observer', id: string) {
    const list = role === 'manager' ? managerIds : role === 'judge' ? judgeIds : observerIds
    list.value = list.value.filter(x => x !== id)
  }

  const saving = ref(false)

  async function save() {
    if (!permissions.value) return
    saving.value = true
    try {
      const data = await api.api.v1.admin.competitions.byCompetitionId(competitionId).patch({
          permissions: {
            ownerId: permissions.value.ownerId!,
            managerIds: managerIds.value,
            judgeIds: judgeIds.value,
            observerIds: observerIds.value,
          },
        });
      permissions.value = data?.permissions ?? permissions.value
      toast.success(describeMessage("administration.label.permissionsSaved"))
    }
    catch (e) {
      toastWriteError(e)
    }
    finally {
      saving.value = false
    }
  }

  const transferTarget = ref<string>('')

  const transferConfirm = ref(false)

  const transferring = ref(false)

  async function transfer() {
    if (!transferTarget.value) return
    transferring.value = true
    try {
      const nextManagers = new Set(managerIds.value)
      nextManagers.add(permissions.value!.ownerId!)
      await api.api.v1.admin.competitions.byCompetitionId(competitionId).patch({
          permissions: {
            ownerId: transferTarget.value,
            managerIds: [...nextManagers],
            judgeIds: judgeIds.value,
            observerIds: observerIds.value,
          },
        });
      toast.success(describeMessage("administration.label.ownershipTransferred"))
      transferConfirm.value = false
      await Promise.all([load(), refresh()])
    }
    catch (e) {
      toast.error(parseApiError(e).displayMessage)
    }
    finally {
      transferring.value = false
    }
  }

  const roles = [
    { key: 'manager' as const, label: "common.label.manager", list: managerIds },
    { key: 'judge' as const, label: "common.label.judge.idPermissionsPage", list: judgeIds },
    { key: 'observer' as const, label: "common.label.observer.idPermissionsPage", list: observerIds },
  ]

  onMounted(load)

  const viewBindings = {
      competitionId,
      adminUserPath,
      X,
      canManagePermissions,
      permissions,
      candidates,
      loading,
      error,
      user,
      candidateName,
      search,
      filteredCandidates,
      assigned,
      add,
      remove,
      saving,
      save,
      transferTarget,
      transferConfirm,
      transferring,
      transfer,
      roles
    }
  const viewState = proxyRefs(viewBindings)

  function onClickTransferConfirm(value: typeof viewState.transferConfirm) {
    viewState.transferConfirm = value
  }

  return { ...viewBindings, onClickTransferConfirm }
}

export type AdminCompetitionsByIdPermissionsPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useAdminCompetitionsByIdPermissionsPage>>>
