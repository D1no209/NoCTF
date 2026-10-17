import { getTheorySession, listTheoryActivities } from '~/api'
import type { NoCtfApplicationTheoryActivitiesTheoryActivitySummary as Activity, NoCtfApplicationTheoryPapersTheoryPaperView as PaperView } from '~/api'
import { message, type UiMessage } from '~/utils/i18n'
import { parseApiError } from '~/utils/api-error'
import { activityStateLabel, scoringLabel } from './theory-labels'

export function useTheoryActivityCatalogPage() {
  const route = useRoute(), router = useRouter(), { canOrganize } = useAuth()
  const staff = computed(() => route.path.startsWith('/admin/'))
  const rows = ref<Activity[]>([]), search = ref(''), page = ref(1), limit = ref(20), total = ref(0)
  const loading = ref(false), error = ref<UiMessage | null>(null)
  /** 探测到的每题会话,用于不可作答时的摘要弹窗。 */
  const sessions = ref<Record<string, PaperView>>({})
  const sessionErrors = ref<Record<string, UiMessage>>({})
  const sessionRequests = new Map<string, number>()
  const summaryOpen = ref(false)
  const summaryActivity = ref<Activity | null>(null)
  /** 摘要弹窗的展示值(分钟换算、缺省值与作答次数)属于功能层转换。 */
  const summary = computed(() => {
    const activity = summaryActivity.value
    const id = activity?.id
    const session = id ? sessions.value[id] : undefined
    return {
      questionCount: activity?.questionCount ?? 0,
      maximumPaperPoints: activity?.maximumPaperPoints ?? 0,
      minutes: Math.round((activity?.durationSeconds ?? 0) / 60),
      score: session?.paper?.score ?? 0,
      attemptUsed: id ? session?.issuedCount ?? 0 : 0,
      attemptLimit: activity?.attemptLimit ?? 1,
    }
  })
  function openSummary(activity: Activity) { summaryActivity.value = activity; summaryOpen.value = true }
  function closeSummary() { summaryOpen.value = false }
  let epoch = 0, timer: ReturnType<typeof setTimeout> | undefined
  async function loadSession(id: string | undefined, current = epoch): Promise<void> {
    if (!id || staff.value || sessionRequests.get(id) === current) return
    sessionRequests.set(id, current)
    delete sessions.value[id]
    delete sessionErrors.value[id]
    try {
      const response = await getTheorySession({ path: { activityId: id } })
      if (response.error || !response.data) throw response.error
      if (current === epoch) sessions.value[id] = response.data
    }
    catch (cause) {
      if (current === epoch) sessionErrors.value[id] = parseApiError(cause, message('theory.loadFailed')).displayMessage
    }
    finally { if (sessionRequests.get(id) === current) sessionRequests.delete(id) }
  }
  async function load() {
    const current = ++epoch; loading.value = true
    sessionRequests.clear()
    try {
      const response = await listTheoryActivities({ query: { staff: staff.value, offset: (page.value - 1) * limit.value, limit: limit.value, desc: true, keyword: search.value } })
      if (response.error || !response.data) throw response.error
      if (current === epoch) {
        rows.value = response.data.items ?? []; total.value = response.data.total ?? 0; error.value = null
        sessions.value = {}
        sessionErrors.value = {}
        if (!staff.value) void Promise.all(rows.value.map(row => loadSession(row.id, current)))
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
  onMounted(load); onBeforeUnmount(() => { epoch++; sessionRequests.clear(); clearTimeout(timer) })
  return { staff, rows, sessions, sessionErrors, retrySession: loadSession, summaryOpen, summaryActivity, summary, openSummary, closeSummary, search, page, limit, total, loading, error, canOrganize, open, load, setPage, setLimit, activityStateLabel, scoringLabel,
    pageCount: computed(() => Math.max(1, Math.ceil(total.value / limit.value))),
    create: () => router.push('/admin/theory/activities/new'), manage: () => router.push('/admin/theory/activities'),
    participate: () => router.push('/theory') }
}
export type TheoryActivityCatalogState = import('vue').ShallowUnwrapRef<ReturnType<typeof useTheoryActivityCatalogPage>>
