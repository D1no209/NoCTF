import { message as describeMessage } from '../../../../../utils/i18n'
import type { UiMessage } from '../../../../../utils/i18n'
import { ChartNoAxesCombined } from '@lucide/vue'


import { toast } from '../../../../../utils/message-toast'
import { adminGetCompetition, adminPatchCompetition } from '../../../../../api'
import type { NoCtfapiEndpointsAdministrationCompetitionsCompetitionLeaderboardVisibilityResponse } from '../../../../../api'
import { useCompetitionAdmin } from '../../../../../lib/admin-competition'

/** Owns state, effects and commands for AdminCompetitionsByIdLeaderboardPage. */
export function useAdminCompetitionsByIdLeaderboardPage() {
  const { competitionId, canWrite } = useCompetitionAdmin()
  const scoreboardPath = `/competitions/${competitionId}/leaderboard`

  const current = ref<NoCtfapiEndpointsAdministrationCompetitionsCompetitionLeaderboardVisibilityResponse | null>(null)

  const loading = ref(true)

  const error = ref<UiMessage | null>(null)

  const frozenStartAt = ref('')

  const hiddenStartAt = ref('')

  const reason = ref('')

  const saving = ref(false)

  async function load() {
    loading.value = true
    error.value = null
    const { data, error: e } = await adminGetCompetition({ path: { competitionId } })
    if (e) error.value = parseApiError(e).displayMessage
    else {
      current.value = data?.leaderboardVisibility ?? null
      frozenStartAt.value = isoToLocalInput(data?.leaderboardVisibility?.frozenStartAt)
      hiddenStartAt.value = isoToLocalInput(data?.leaderboardVisibility?.hiddenStartAt)
    }
    loading.value = false
  }

  async function save() {
    if (!current.value) return
    saving.value = true
    try {
      const { data, error } = await adminPatchCompetition({
        path: { competitionId },
        body: {
          leaderboardVisibility: {
            frozenStartAt: localInputToIso(frozenStartAt.value) ?? null,
            hiddenStartAt: localInputToIso(hiddenStartAt.value) ?? null,
            reason: reason.value.trim() || null,
          },
        },
      })
      if (error) throw error
      current.value = data?.leaderboardVisibility ?? current.value
      frozenStartAt.value = isoToLocalInput(current.value?.frozenStartAt)
      hiddenStartAt.value = isoToLocalInput(current.value?.hiddenStartAt)
      reason.value = ''
      toast.success(describeMessage("leaderboard.label.scoreboardVisibilityUpdated"))
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
      ChartNoAxesCombined,
      scoreboardPath,
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
