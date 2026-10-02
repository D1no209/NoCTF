import { adminUserPath } from '~/features/admin/admin-navigation'
import { proxyRefs } from 'vue'

import { X } from '@lucide/vue'
import { toast } from 'vue-sonner'
import { adminGetCompetition, adminListCompetitionPermissionCandidates, adminPatchCompetition } from '../../../../../api'
import type { NoCtfapiEndpointsAdministrationCompetitionsCompetitionPermissionCandidateResponse, NoCtfapiEndpointsAdministrationCompetitionsCompetitionPermissionsResponse } from '../../../../../api'
import { useCompetitionAdmin } from '../../../../../lib/admin-competition'

/** Owns state, effects and commands for AdminCompetitionsByIdPermissionsPage. */
export function useAdminCompetitionsByIdPermissionsPage() {
  const { competitionId, canManagePermissions, refresh } = useCompetitionAdmin()

  const permissions = ref<NoCtfapiEndpointsAdministrationCompetitionsCompetitionPermissionsResponse | null>(null)

  const candidates = ref<NoCtfapiEndpointsAdministrationCompetitionsCompetitionPermissionCandidateResponse[]>([])

  const loading = ref(true)

  const error = ref<string | null>(null)

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
    const [perm, cand] = await Promise.all([
      adminGetCompetition({ path: { competitionId } }),
      adminListCompetitionPermissionCandidates({ path: { competitionId } }),
    ])
    if (perm.error) {
      error.value = parseApiError(perm.error).message
      loading.value = false
      return
    }
    permissions.value = perm.data?.permissions ?? null
    candidates.value = cand.data?.items ?? []
    managerIds.value = [...(perm.data?.permissions?.managerIds ?? [])]
    judgeIds.value = [...(perm.data?.permissions?.judgeIds ?? [])]
    observerIds.value = [...(perm.data?.permissions?.observerIds ?? [])]
    loading.value = false
  }

  const search = ref('')

  const filteredCandidates = computed(() => {
    const q = search.value.trim().toLowerCase()
    const list = candidates.value.filter(c => c.id && c.id !== permissions.value?.ownerId)
    if (!q) return list.slice(0, 20)
    return list.filter(c => c.userName?.toLowerCase().includes(q)).slice(0, 20)
  })

  function assigned(id?: string): boolean {
    if (!id) return true
    return managerIds.value.includes(id) || judgeIds.value.includes(id) || observerIds.value.includes(id)
  }

  function add(role: 'manager' | 'judge' | 'observer', id?: string) {
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
      const { data, error } = await adminPatchCompetition({
        path: { competitionId },
        body: {
          permissions: {
            ownerId: permissions.value.ownerId!,
            managerIds: managerIds.value,
            judgeIds: judgeIds.value,
            observerIds: observerIds.value,
          },
        },
      })
      if (error) throw error
      permissions.value = data?.permissions ?? permissions.value
      toast.success(translate("ui.permissionsSaved"))
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
      const { error } = await adminPatchCompetition({
        path: { competitionId },
        body: {
          permissions: {
            ownerId: transferTarget.value,
            managerIds: [...nextManagers],
            judgeIds: judgeIds.value,
            observerIds: observerIds.value,
          },
        },
      })
      if (error) throw error
      toast.success(translate("ui.ownershipHasBeenTransferred"))
      transferConfirm.value = false
      await Promise.all([load(), refresh()])
    }
    catch (e) {
      toast.error(parseApiError(e).message)
    }
    finally {
      transferring.value = false
    }
  }

  const roles = [
    { key: 'manager' as const, label: "ui.manager", list: managerIds },
    { key: 'judge' as const, label: "ui.judge2", list: judgeIds },
    { key: 'observer' as const, label: "ui.observer2", list: observerIds },
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
