import { markRaw } from 'vue'
import { listStaffWorkItemsEndpoint } from '../../../api'
import type { NoCtfApplicationCompetitionsStaffWebhooksStaffWorkItemView as Item } from '../../../api'
import { CompetitionAdminKey } from '../../../lib/admin-competition'
import type { CompetitionAdminRole } from '../../../lib/admin-competition'
import { useOffsetPagination } from '../../../composables/useOffsetPagination'
import { watchCompetition } from '../../../composables/useCompetitionHub'
import CheatsPage from '../../routes/admin/competitions/[id]/AdminCompetitionsByIdCheatsPage.vue'
import TeamsPage from '../../routes/admin/competitions/[id]/AdminCompetitionsByIdTeamsPage.vue'
import StaffWebhooks from './StaffWebhooks.vue'
import { message } from '../../../utils/i18n'
import type { UiMessage } from '../../../utils/i18n'

export function useStaffWorkbench() {
  const route = useRoute(); const router = useRouter(); const auth = useAuth()
  const competitionId = route.params.id as string
  const shared = useAdminCompetitionResource(competitionId)
  const competition = shared.competition
  const error = shallowRef<UiMessage | null>(null)
  const ready = ref(false)
  const role = computed<CompetitionAdminRole>(() => auth.isAdministrator.value || competition.value?.ownerId === auth.user.value?.userId
    ? 'Owner' : competition.value?.administrationRole ?? 'Observer')
  async function refresh() {
    const result = await shared.refresh()
    if (result.error || !result.data?.competition) { error.value = parseApiError(result.error).displayMessage; ready.value = false; return }
    error.value = null; ready.value = true
  }
  provide(CompetitionAdminKey, { data: shared.data, read: shared.read, accept: shared.accept, competitionId, competition, role, canWrite: computed(() => shared.fresh.value && (role.value === 'Owner' || role.value === 'Manager')),
    canJudge: computed(() => shared.fresh.value && role.value !== 'Observer'), canManagePermissions: computed(() => shared.fresh.value && role.value === 'Owner'), refresh })
  const panel = computed(() => route.query.kind === 'CheatIncident' ? 'CheatIncident' : route.query.kind === 'BanAppeal' ? 'BanAppeal' : route.query.kind === 'Webhooks' ? 'Webhooks' : 'Pending')
  const pending = useOffsetPagination<Item>(async ({ offset, limit, desc }) => {
    const result = await listStaffWorkItemsEndpoint({ path: { competitionId }, query: { offset, limit, desc, pendingOnly: true } })
    if (result.error || !result.data) throw result.error
    return { items: result.data.items ?? [], total: result.data.total ?? 0 }
  }, { initialPageSize: 20, initialDesc: false })
  async function load() { await refresh(); if (ready.value) await pending.loadPage(pending.page.value) }
  function choose(kind: typeof panel.value) { void router.push({ query: { ...route.query, kind, incident: undefined, appeal: undefined, item: undefined } }) }
  function itemPath(item: Item) { if (!item.managementUrl) return ''; const url = new URL(item.managementUrl); return url.pathname + url.search }
  const kindKeys = { 0: 'staffWebhook.cheatIncident', 1: 'staffWebhook.consultation', 2: 'staffWebhook.banAppeal' } as const
  let unsubscribe: (() => void) | undefined
  onMounted(() => { void load(); unsubscribe = watchCompetition(competitionId, {
    competitionEventChanged: () => void pending.loadPage(pending.page.value), onReconnected: () => void load() }) })
  onBeforeUnmount(() => unsubscribe?.())
  return { competitionId, competition, error, ready, panel, pending, kindKeys, choose, itemPath, load,
    CheatsPage: markRaw(CheatsPage), TeamsPage: markRaw(TeamsPage), StaffWebhooks: markRaw(StaffWebhooks), waitingLabel: message('staffWebhook.pending') }
}
export type StaffWorkbenchViewState = import('vue').ShallowUnwrapRef<ReturnType<typeof useStaffWorkbench>>
