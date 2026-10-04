import { dateObject } from '../../../../../utils/date-value'

import { api } from '../../../../../lib/api'
import { message as describeMessage } from '../../../../../utils/i18n'
import type { UiMessage } from '../../../../../utils/i18n'


import { toast } from '../../../../../utils/message-toast'

import type { NoCTFAPIEndpointsAdministrationCompetitionsCompetitionLeaderboardVisibilityResponse } from '../../../../../api/models'
import { useCompetitionAdmin } from '../../../../../lib/admin-competition'

/** Owns state, effects and commands for AdminCompetitionsByIdLeaderboardPage. */
export function useAdminCompetitionsByIdLeaderboardPage() {
  const { competitionId, canWrite } = useCompetitionAdmin()

  const current = ref<NoCTFAPIEndpointsAdministrationCompetitionsCompetitionLeaderboardVisibilityResponse | null>(null)

  const loading = ref(true)

  const error = ref<UiMessage | null>(null)

  const frozenStartAt = ref('')

  const hiddenStartAt = ref('')

  const reason = ref('')

  const saving = ref(false)

  async function load() {
    loading.value = true
    error.value = null
    let e: unknown;
    const data = await api.api.v1.admin.competitions.byCompetitionId(competitionId).get().catch(cause => { e = cause; return undefined });
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
      const data = await api.api.v1.admin.competitions.byCompetitionId(competitionId).patch({
          leaderboardVisibility: {
            frozenStartAt: dateObject(localInputToIso(frozenStartAt.value) ?? null),
            hiddenStartAt: dateObject(localInputToIso(hiddenStartAt.value) ?? null),
            reason: reason.value.trim() || null,
          },
        });
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
