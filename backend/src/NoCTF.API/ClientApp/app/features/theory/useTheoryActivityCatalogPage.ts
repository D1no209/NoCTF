import { listTheoryActivities } from '~/api'
import type { NoCtfApplicationTheoryActivitiesTheoryActivitySummary as Activity } from '~/api'
import { message, type UiMessage } from '~/utils/i18n'
import { parseApiError } from '~/utils/api-error'
import { activityStateLabel, scoringLabel } from './theory-labels'

export function useTheoryActivityCatalogPage() {
  const route = useRoute(), router = useRouter(), { canOrganize } = useAuth()
  const staff = computed(() => route.path.startsWith('/admin/'))
  const rows = ref<Activity[]>([]), search = ref(''), page = ref(1), limit = ref(20), total = ref(0)
  const loading = ref(false), error = ref<UiMessage | null>(null)
  let epoch = 0, timer: ReturnType<typeof setTimeout> | undefined
  async function load() {
    const current = ++epoch; loading.value = true
    try {
      const response = await listTheoryActivities({ query: { staff: staff.value, offset: (page.value - 1) * limit.value, limit: limit.value, desc: true, keyword: search.value } })
      if (response.error || !response.data) throw response.error
      if (current === epoch) { rows.value = response.data.items ?? []; total.value = response.data.total ?? 0; error.value = null }
    }
    catch (cause) { if (current === epoch) error.value = parseApiError(cause, message('theory.loadFailed')).displayMessage }
    finally { if (current === epoch) loading.value = false }
  }
  function setPage(value: number) { page.value = value; void load() }
  function setLimit(value: number) { limit.value = value; setPage(1) }
  function open(activity: Activity) { if (activity.id) void router.push(staff.value ? `/admin/theory/activities/${activity.id}` : `/theory/${activity.id}`) }
  watch(search, () => { clearTimeout(timer); timer = setTimeout(() => setPage(1), 250) })
  onMounted(load); onBeforeUnmount(() => { epoch++; clearTimeout(timer) })
  return { staff, rows, search, page, limit, total, loading, error, canOrganize, open, load, setPage, setLimit, activityStateLabel, scoringLabel,
    pageCount: computed(() => Math.max(1, Math.ceil(total.value / limit.value))),
    create: () => router.push('/admin/theory/activities/new'), manage: () => router.push('/admin/theory/activities'),
    participate: () => router.push('/theory') }
}
export type TheoryActivityCatalogState = import('vue').ShallowUnwrapRef<ReturnType<typeof useTheoryActivityCatalogPage>>
