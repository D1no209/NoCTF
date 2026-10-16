import { getTheorySession, listTheoryActivities } from '~/api'
import type { NoCtfApplicationTheoryActivitiesTheoryActivitySummary as Activity } from '~/api'
import { message, type UiMessage } from '~/utils/i18n'
import { parseApiError } from '~/utils/api-error'
import { activityStateLabel, scoringLabel } from './theory-labels'

export function useTheoryActivityCatalogPage() {
  const route = useRoute(), router = useRouter(), { canOrganize } = useAuth()
  const staff = computed(() => route.path.startsWith('/admin/'))
  const rows = ref<Activity[]>([]), search = ref(''), page = ref(1), limit = ref(20), total = ref(0)
  const loading = ref(false), error = ref<UiMessage | null>(null)
  /** 每人每活动的作答状态:列表载荷不含该信息,按当前页逐行探一次 session。 */
  const started = ref<Record<string, boolean>>({})
  async function probeStarted(): Promise<void> {
    if (staff.value) return
    await Promise.all(rows.value.filter(row => row.id).map(async (row) => {
      const id = row.id!
      try {
        const response = await getTheorySession({ path: { activityId: id } })
        started.value[id] = Boolean(response.data?.paper) || (response.data?.issuedCount ?? 0) > 0
      }
      catch { started.value[id] = true }
    }))
  }
  let epoch = 0, timer: ReturnType<typeof setTimeout> | undefined
  async function load() {
    const current = ++epoch; loading.value = true
    try {
      const response = await listTheoryActivities({ query: { staff: staff.value, offset: (page.value - 1) * limit.value, limit: limit.value, desc: true, keyword: search.value } })
      if (response.error || !response.data) throw response.error
      if (current === epoch) {
        rows.value = response.data.items ?? []; total.value = response.data.total ?? 0; error.value = null
        started.value = {}
        void probeStarted()
      }
    }
    catch (cause) { if (current === epoch) error.value = parseApiError(cause, message('theory.loadFailed')).displayMessage }
    finally { if (current === epoch) loading.value = false }
  }
  function setPage(value: number) { page.value = value; void load() }
  function setLimit(value: number) { limit.value = value; setPage(1) }
  function open(activity: Activity, newPaper = false) {
    if (!activity.id) return
    if (staff.value) { void router.push(`/admin/theory/activities/${activity.id}`); return }
    void router.push({ path: `/theory/${activity.id}`, query: newPaper ? { new: '1' } : {} })
  }
  watch(search, () => { clearTimeout(timer); timer = setTimeout(() => setPage(1), 250) })
  onMounted(load); onBeforeUnmount(() => { epoch++; clearTimeout(timer) })
  return { staff, rows, started, search, page, limit, total, loading, error, canOrganize, open, load, setPage, setLimit, activityStateLabel, scoringLabel,
    pageCount: computed(() => Math.max(1, Math.ceil(total.value / limit.value))),
    create: () => router.push('/admin/theory/activities/new'), manage: () => router.push('/admin/theory/activities'),
    participate: () => router.push('/theory') }
}
export type TheoryActivityCatalogState = import('vue').ShallowUnwrapRef<ReturnType<typeof useTheoryActivityCatalogPage>>
