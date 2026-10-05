import { message as describeMessage } from '../../../utils/i18n'
import type { UiMessage } from '../../../utils/i18n'
import { computed, markRaw, ref, watch } from 'vue'
import { Plus } from '@lucide/vue'
import { adminListCompetitions, listCompetitionsEndpoint } from '../../../api'
import type { NoCtfapiEndpointsCompetitionsCompetitionResponse } from '../../../api'
import { resolveCompetitionBrowser, type CompetitionGroup } from '../../competitions/competition-browser'
import CompetitionOverviewComponent from '../../competitions/CompetitionOverview.vue'
import CompetitionSidebarComponent from '../../competitions/CompetitionSidebar.vue'
import CreateCompetitionDialogComponent from '../../competitions/CreateCompetitionDialog.vue'
import { competitionPath, competitionsPath } from '../../../utils/app-routes'

/** Owns list loading and URL-backed selection; the overview owns its own requests and forms. */
export function useCompetitionsIndexPage() {
  const route = useRoute()
  if ('competition' in route.query) throw createError({ statusCode: 404, statusMessage: 'Page not found' })
  const router = useRouter()
  const { isAdministrator } = useAuth()
  const items = ref<NoCtfapiEndpointsCompetitionsCompetitionResponse[]>([])
  const loading = ref(true)
  const error = ref<UiMessage | null>(null)
  const queryValue = (value: unknown) => typeof value === 'string' ? value : null
  const routeCompetitionId = computed(() => queryValue(route.params.id))
  const requestedCompetitionId = routeCompetitionId
  const browser = computed(() => resolveCompetitionBrowser(items.value, requestedCompetitionId.value, queryValue(route.query.group), isAdministrator.value))
  const running = computed(() => browser.value.groups.running)
  const upcoming = computed(() => browser.value.groups.upcoming)
  const finished = computed(() => browser.value.groups.finished)
  const deleted = computed(() => browser.value.groups.deleted)
  const counts = computed(() => ({ running: running.value.length, upcoming: upcoming.value.length, finished: finished.value.length, deleted: deleted.value.length }))
  const group = computed({
    get: () => browser.value.group,
    set: (value: CompetitionGroup) => {
      void router.push({ path: competitionsPath, query: { ...route.query, group: value } })
    },
  })
  const selected = computed(() => browser.value.selected)
  const selectedId = computed(() => selected.value?.id ?? null)
  const missing = computed(() => browser.value.missing)
  const createOpen = ref(route.query.create === '1')
  const options = computed(() => browser.value.items.map(competition => ({
    value: competition.id!, label: competition.title ?? '', competition,
  })))
  function selectCompetition(value: string) {
    if (value === selectedId.value) return
    const { group: _group, ...query } = route.query
    void router.push({ path: competitionPath(value), query })
  }
  function openCreateDialog() {
    createOpen.value = true
  }
  function setCreateOpen(value: boolean) {
    createOpen.value = value
    if (!value && route.query.create === '1') {
      const { create: _create, ...query } = route.query
      void router.replace({ query })
    }
  }
  function handleCompetitionCreated(competition: NoCtfapiEndpointsCompetitionsCompetitionResponse) {
    if (!competition.id) {
      void load()
      return
    }
    items.value = [competition, ...items.value.filter(item => item.id !== competition.id)]
    const { create: _create, group: _group, ...query } = route.query
    void router.push({ path: competitionPath(competition.id), query })
  }
  let loadGeneration = 0
  async function load() {
    const generation = ++loadGeneration
    loading.value = true
    error.value = null
    try {
      const { data, error: failure } = isAdministrator.value
        ? await adminListCompetitions({ query: { includeDeleted: true } })
        : await listCompetitionsEndpoint()
      if (failure || !data) throw failure
      if (generation !== loadGeneration) return
      items.value = data.items ?? []
    } catch (failure) {
      if (generation !== loadGeneration) return
      error.value = parseApiError(failure, describeMessage('competitions.competitionsIndex.error.loadContestListFailed')).displayMessage
    } finally {
      if (generation === loadGeneration) loading.value = false
    }
  }
  watch(isAdministrator, () => void load(), { immediate: true })
  watch(() => route.query.create, value => {
    if (value === '1' && isAdministrator.value) createOpen.value = true
  })
  const CompetitionOverview = markRaw(CompetitionOverviewComponent)
  const CompetitionSidebar = markRaw(CompetitionSidebarComponent)
  const CreateCompetitionDialog = markRaw(CreateCompetitionDialogComponent)
  return { Plus, isAdministrator, loading, error, load, counts, group, selected, selectedId, missing, options, selectCompetition, createOpen, openCreateDialog, setCreateOpen, handleCompetitionCreated, CompetitionOverview, CompetitionSidebar, CreateCompetitionDialog }
}
export type CompetitionsIndexPageViewState = import('vue').ShallowUnwrapRef<ReturnType<typeof useCompetitionsIndexPage>>
