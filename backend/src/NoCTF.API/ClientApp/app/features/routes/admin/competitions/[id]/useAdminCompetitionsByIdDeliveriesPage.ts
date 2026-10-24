import { RefreshCw, Copy } from '@lucide/vue'
import { adminListCompetitionDeliveries, adminListCompetitionChallenges, adminListTeams } from '~/api'
import type { NoCtfapiEndpointsAdministrationCompetitionsCompetitionDeliveryResponse as Delivery,
  NoCtfapiEndpointsAdministrationCompetitionsCompetitionDeliveryFlagResponse as Flag } from '~/api'
import { useCompetitionAdmin } from '~/lib/admin-competition'
import { useAdminCompetitionReferences, useAdminCompetitionFilteredList } from '~/features/admin/useAdminCompetitionList'
import { adminTeamPath, adminChallengePath } from '~/features/admin/admin-navigation'
import { adminFormatDateTime } from '~/utils/admin-format'
import { message } from '~/utils/i18n'
import { toast } from '~/utils/message-toast'

export function useAdminCompetitionsByIdDeliveriesPage() {
  const { competitionId } = useCompetitionAdmin()
  const { challengeOptions, teamOptions, loadRefs } = useAdminCompetitionReferences(competitionId,
    { listChallenges: adminListCompetitionChallenges, listTeams: adminListTeams })
  const filterChallenge = ref('')
  const filterTeam = ref('')
  const includeHistory = ref(false)
  const list = useAdminCompetitionFilteredList<Delivery, { challenge: string; team: string; history: string }>(async (filters, pagination) => {
    const result = await adminListCompetitionDeliveries({ path: { competitionId }, query: {
      competitionChallengeId: filters.challenge || null, teamId: filters.team || null,
      includeHistory: filters.history === 'true', ...pagination,
    } })
    if (result.error || !result.data) throw parseApiError(result.error)
    return { items: result.data.items ?? [], total: result.data.total ?? 0 }
  }, { readFilters: () => ({ challenge: filterChallenge.value, team: filterTeam.value, history: String(includeHistory.value) }) })
  function clear() { filterChallenge.value = ''; filterTeam.value = ''; includeHistory.value = false; return list.applyFilters() }
  function refresh() { return list.pagination.loadPage() }
  function attachmentName(item: Delivery, flag: Flag) {
    return item.attachments?.find(file => file.id === flag.attachmentId)?.fileName ?? translate('challengeDelivery.attachmentUnavailable')
  }
  function stateKey(flag: Flag) { return `runtimeFlags.state.${flag.state ?? 'Active'}` as const }
  async function copy(flag: Flag) {
    if (!flag.flag) return
    try { await navigator.clipboard.writeText(flag.flag); toast.success(message('common.label.copiedClipboard')) }
    catch { toast.error(message('common.kohPanel.error.copyManuallySelectFailed')) }
  }
  onMounted(() => { void loadRefs(); void list.pagination.loadPage(1) })
  onBeforeUnmount(() => list.pagination.reset())
  return { RefreshCw, Copy, competitionId, challengeOptions, teamOptions, filterChallenge, filterTeam, includeHistory,
    ...list.listBindings, applyFilters: list.applyFilters, clear, refresh, attachmentName, stateKey, copy,
    adminTeamPath, adminChallengePath, adminFormatDateTime }
}
export type AdminCompetitionsByIdDeliveriesPageViewState = import('vue').ShallowUnwrapRef<ReturnType<typeof useAdminCompetitionsByIdDeliveriesPage>>
