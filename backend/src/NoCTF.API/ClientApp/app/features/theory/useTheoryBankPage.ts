import { theoryQuestionsList } from '~/api'
import type { NoCtfApplicationTheoryQuestionsTheoryQuestionSummary as Question } from '~/api'
import { parseApiError } from '~/utils/api-error'
import { message, type UiMessage } from '~/utils/i18n'
import { questionKindLabel, questionStateLabel } from './theory-labels'

export function useTheoryBankPage() {
  const router = useRouter(), route = useRoute(), { canOrganize } = useAuth()
  const search = ref(typeof route.query.q === 'string' ? route.query.q : '')
  const kind = ref(String(route.query.kind ?? 'all')), status = ref(String(route.query.status ?? 'all')), difficulty = ref(String(route.query.difficulty ?? 'all'))
  const knowledge = ref(String(route.query.knowledge ?? '')), tag = ref(String(route.query.tag ?? ''))
  const asEnum = (value: string) => value === 'all' ? undefined : Number(value) as 0 | 1 | 2
  const page = ref(Number(route.query.page) || 1), limit = ref(20), total = ref(0)
  const rows = ref<Question[]>([]), loading = ref(false), error = ref<UiMessage | null>(null)
  let epoch = 0, timer: ReturnType<typeof setTimeout> | undefined
  async function load() {
    const current = ++epoch; loading.value = true; error.value = null
    try {
      const { data, error: failure } = await theoryQuestionsList({ query: { keyword: search.value, kind: asEnum(kind.value), state: asEnum(status.value), difficulty: asEnum(difficulty.value), knowledge: knowledge.value || undefined, tag: tag.value || undefined, offset: (page.value - 1) * limit.value, limit: limit.value, desc: true } })
      if (failure || !data) throw failure
      if (current === epoch) { rows.value = data.items ?? []; total.value = data.total ?? 0 }
    }
    catch (failure) { if (current === epoch) error.value = parseApiError(failure, message('theory.loadFailed')).displayMessage }
    finally { if (current === epoch) loading.value = false }
  }
  function setPage(value: number) { page.value = value; void load(); void router.replace({ query: { ...route.query, page: value, q: search.value || undefined, kind: kind.value === 'all' ? undefined : kind.value, status: status.value === 'all' ? undefined : status.value, difficulty: difficulty.value === 'all' ? undefined : difficulty.value, knowledge: knowledge.value || undefined, tag: tag.value || undefined } }) }
  function setLimit(value: number) { limit.value = value; setPage(1) }
  watch([search, kind, status, difficulty, knowledge, tag], () => { clearTimeout(timer); timer = setTimeout(() => setPage(1), 250) })
  function create() { void router.push('/admin/theory/questions/new') }
  function open(row: Question) { if (row.id && row.canReadAnswers) void router.push(`/admin/theory/questions/${row.id}`) }
  onMounted(load)
  onBeforeUnmount(() => { epoch++; clearTimeout(timer) })
  return { kind, status, difficulty, knowledge, tag, kinds: [0, 1, 2] as const, difficultyLabel: (value?: number) => (['theory.basic', 'theory.intermediate', 'theory.advanced'] as const)[value ?? 0] ?? 'theory.basic', classifications: (row: Question) => (row.classifications ?? []).map(item => item.name).join(' · '), rows, search, page, limit, total, loading, error, canOrganize, load, create, open, setPage, setLimit,
    pageCount: computed(() => Math.max(1, Math.ceil(total.value / limit.value))), questionKindLabel, questionStateLabel }
}
export type TheoryBankPageState = import('vue').ShallowUnwrapRef<ReturnType<typeof useTheoryBankPage>>
