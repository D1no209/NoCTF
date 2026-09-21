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
  const router = useRouter()
  const { isAdministrator } = useAuth()
  const items = ref<NoCtfapiEndpointsCompetitionsCompetitionResponse[]>([])
  const loading = ref(true)
  const error = ref<string | null>(null)
  const queryValue = (value: unknown) => typeof value === 'string' ? value : null
  const routeCompetitionId = computed(() => queryValue(route.params.id))
  const legacyCompetitionId = computed(() => queryValue(route.query.competition))
  const requestedCompetitionId = computed(() => routeCompetitionId.value ?? legacyCompetitionId.value)
  const browser = computed(() => resolveCompetitionBrowser(items.value, requestedCompetitionId.value, queryValue(route.query.group), isAdministrator.value))
  const running = computed(() => browser.value.groups.running)
  const upcoming = computed(() => browser.value.groups.upcoming)
  const finished = computed(() => browser.value.groups.finished)
  const deleted = computed(() => browser.value.groups.deleted)
  const counts = computed(() => ({ running: running.value.length, upcoming: upcoming.value.length, finished: finished.value.length, deleted: deleted.value.length }))
  const group = computed({
    get: () => browser.value.group,
    set: (value: CompetitionGroup) => {
      const { competition: _selected, ...query } = route.query
      void router.push({ path: competitionsPath, query: { ...query, group: value } })
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
    const { competition: _selected, group: _group, ...query } = route.query
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
    const { competition: _selected, create: _create, group: _group, ...query } = route.query
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
      error.value = parseApiError(failure, translate('ui.failedToLoadContestList')).message
    } finally {
      if (generation === loadGeneration) loading.value = false
    }
  }
  watch(isAdministrator, () => void load(), { immediate: true })
  watch(() => route.query.create, value => {
    if (value === '1' && isAdministrator.value) createOpen.value = true
  })
  onMounted(() => {
    if (!routeCompetitionId.value && legacyCompetitionId.value) {
      const { competition: _selected, group: _group, ...query } = route.query
      void router.replace({ path: competitionPath(legacyCompetitionId.value), query })
    }
  })
  const CompetitionOverview = markRaw(CompetitionOverviewComponent)
  const CompetitionSidebar = markRaw(CompetitionSidebarComponent)
  const CreateCompetitionDialog = markRaw(CreateCompetitionDialogComponent)
  return { Plus, isAdministrator, loading, error, load, counts, group, selected, selectedId, missing, options, selectCompetition, createOpen, openCreateDialog, setCreateOpen, handleCompetitionCreated, CompetitionOverview, CompetitionSidebar, CreateCompetitionDialog }
}
export type CompetitionsIndexPageViewState = import('vue').ShallowUnwrapRef<ReturnType<typeof useCompetitionsIndexPage>>
