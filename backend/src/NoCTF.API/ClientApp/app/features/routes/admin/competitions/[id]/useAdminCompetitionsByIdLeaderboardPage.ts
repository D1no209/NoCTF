

import { toast } from 'vue-sonner'
import { adminGetCompetitionLeaderboardVisibility, adminUpdateCompetitionLeaderboardVisibility } from '../../../../../api'
import type { NoCtfapiEndpointsAdministrationCompetitionsCompetitionLeaderboardVisibilityResponse } from '../../../../../api'
import { useCompetitionAdmin } from '../../../../../lib/admin-competition'

/** Owns state, effects and commands for AdminCompetitionsByIdLeaderboardPage. */
export function useAdminCompetitionsByIdLeaderboardPage() {
  const { competitionId, canWrite } = useCompetitionAdmin()

  const current = ref<NoCtfapiEndpointsAdministrationCompetitionsCompetitionLeaderboardVisibilityResponse | null>(null)

  const loading = ref(true)

  const error = ref<string | null>(null)

  const frozenStartAt = ref('')

  const hiddenStartAt = ref('')

  const reason = ref('')

  const saving = ref(false)

  async function load() {
    loading.value = true
    error.value = null
    const { data, error: e } = await adminGetCompetitionLeaderboardVisibility({ path: { competitionId } })
    if (e) error.value = parseApiError(e).message
    else {
      current.value = data ?? null
      frozenStartAt.value = isoToLocalInput(data?.frozenStartAt)
      hiddenStartAt.value = isoToLocalInput(data?.hiddenStartAt)
    }
    loading.value = false
  }

  async function save() {
    if (!current.value) return
    saving.value = true
    try {
      const { data, error } = await adminUpdateCompetitionLeaderboardVisibility({
        path: { competitionId },
        body: {
          frozenStartAt: localInputToIso(frozenStartAt.value) ?? null,
          hiddenStartAt: localInputToIso(hiddenStartAt.value) ?? null,
          reason: reason.value.trim() || null,
        },
      })
      if (error) throw error
      current.value = data ?? current.value
      frozenStartAt.value = isoToLocalInput(current.value.frozenStartAt)
      hiddenStartAt.value = isoToLocalInput(current.value.hiddenStartAt)
      reason.value = ''
      toast.success(translate("ui.scoreboardVisibilityUpdated"))
    }
    catch (e) {
      toastWriteError(e)
    }
    finally {
      saving.value = false
    }
  }

  onMounted(load)

  return {
      canWrite,
      current,
      loading,
      error,
      frozenStartAt,
      hiddenStartAt,
      reason,
      saving,
      save
    }
}

export type AdminCompetitionsByIdLeaderboardPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useAdminCompetitionsByIdLeaderboardPage>>>
