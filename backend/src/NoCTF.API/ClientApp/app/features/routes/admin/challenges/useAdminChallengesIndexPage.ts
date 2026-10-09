import type { UiMessage } from '../../../../utils/i18n'
import { markRaw } from 'vue'

import { Filter, Plus, UserRound } from '@lucide/vue'
import { adminChallengeBankListTemplates } from '../../../../api'
import type { NoCtfapiEndpointsAdministrationChallengeBankChallengeTemplateSummaryResponse } from '../../../../api'
import AdminDateTimeComponent from '../../../admin/AdminDateTime.vue'
import AdminGameModeBadgeComponent from '../../../admin/AdminGameModeBadge.vue'
import ChallengeTemplateCreateDialogComponent from '../../../admin/ChallengeTemplateCreateDialog.vue'
import { directionKey, directionLabel } from '../../../../utils/directions'
import { useOffsetPagination } from '../../../../composables/useOffsetPagination'

type ChallengeTemplate = NoCtfapiEndpointsAdministrationChallengeBankChallengeTemplateSummaryResponse

let lastIncludeDeleted = false
interface ChallengeLibrarySnapshot {
  userId: string | null
  includeDeleted: boolean
  onlyMine: boolean
  search: string
  directionFilter: string
  templates: ChallengeTemplate[]
  directions: string[]
  page: number
  limit: number
  total: number
}
let lastSnapshot: ChallengeLibrarySnapshot | null = null
const directionCatalogs = new Map<string, string[]>()

/** Owns state, effects and commands for AdminChallengesIndexPage. */
export function useAdminChallengesIndexPage() {
  const route = useRoute()
  const router = useRouter()

  const { canOrganize, user } = useAuth()

  const createOpen = ref(route.path === '/admin/challenges/new')

  const previous = lastSnapshot?.userId === (user.value?.userId ?? null) ? lastSnapshot : null

  const includeDeleted = ref(route.query.deleted === '1'
    ? true
    : previous?.includeDeleted ?? lastIncludeDeleted)

  const onlyMine = ref(route.query.mine === '1' ? true : previous?.onlyMine ?? false)

  const loadError = ref<UiMessage | null>(null)

  const search = ref(typeof route.query.q === 'string'
    ? route.query.q
    : previous?.search ?? '')

  const directionFilter = ref(typeof route.query.direction === 'string'
    ? route.query.direction
    : previous?.directionFilter ?? 'all')

  const restored = previous
    && previous.includeDeleted === includeDeleted.value
    && previous.onlyMine === onlyMine.value
    && previous.search === search.value
    && previous.directionFilter === directionFilter.value ? previous : null
  const directionCatalogKey = computed(() => `${user.value?.userId ?? ''}:${includeDeleted.value}:${onlyMine.value}`)
  const templates = ref<ChallengeTemplate[]>([...(restored?.templates ?? [])])
  const directions = ref<string[]>([...(restored?.directions
    ?? directionCatalogs.get(directionCatalogKey.value)
    ?? [])])
  let requestGeneration = 0

  const directionOptions = computed(() => [...new Map(directions.value
    .map(value => ({ key: directionKey(value), value, label: directionLabel(value) }))
    .filter(option => option.key)
    .map(option => [option.key, option] as const)).values()]
    .sort((left, right) => left.label.localeCompare(right.label)))

  const filteredTemplates = computed(() => templates.value)

  const pagination = useOffsetPagination<ChallengeTemplate>(async ({ offset, limit, desc }) => {
    const generation = requestGeneration
    const catalogKey = directionCatalogKey.value
    const { data, error } = await adminChallengeBankListTemplates({
      query: {
        includeDeleted: includeDeleted.value,
        onlyMine: onlyMine.value,
        keyword: search.value.trim() || null,
        direction: directionFilter.value === 'all' ? null : directionFilter.value,
        offset,
        limit,
        desc,
      },
    })
    if (generation !== requestGeneration) return { items: [], total: 0 }
    if (error || !data) throw error ?? new Error('Failed to load challenge templates.')
    templates.value = data.items ?? []
    const catalog = [...new Set([
      ...(directionCatalogs.get(catalogKey) ?? []),
      ...directions.value,
      ...(data.directions ?? []),
    ])]
    directionCatalogs.set(catalogKey, catalog)
    directions.value = catalog
    return { items: templates.value, total: data.total ?? 0 }
  }, { initialDesc: true })

  if (restored) {
    pagination.page.value = restored.page
    pagination.limit.value = restored.limit
    pagination.total.value = restored.total
    pagination.items.value = [...restored.templates]
    pagination.initialized.value = true
    directionCatalogs.set(directionCatalogKey.value, [...restored.directions])
  }

  const filterPending = ref(false)
  const loading = computed(() => pagination.loading.value || filterPending.value)

  function rememberSnapshot(): void {
    lastIncludeDeleted = includeDeleted.value
    lastSnapshot = {
      userId: user.value?.userId ?? null,
      includeDeleted: includeDeleted.value,
      onlyMine: onlyMine.value,
      search: search.value,
      directionFilter: directionFilter.value,
      templates: [...templates.value],
      directions: [...directions.value],
      page: pagination.page.value,
      limit: pagination.limit.value,
      total: pagination.total.value,
    }
  }

  async function loadPage(targetPage = pagination.page.value): Promise<void> {
    const generation = requestGeneration
    loadError.value = null
    await pagination.loadPage(targetPage)
    if (generation !== requestGeneration) return
    loadError.value = pagination.error.value?.message ?? null
    if (!pagination.error.value) rememberSnapshot()
  }

  async function load(): Promise<void> {
    await loadPage(pagination.page.value)
  }

  async function setPageSize(value: number): Promise<void> {
    if (!Number.isFinite(value) || value < 1 || value === pagination.limit.value) return
    requestGeneration += 1
    const generation = requestGeneration
    loadError.value = null
    await pagination.setPageSize(value)
    if (generation !== requestGeneration) return
    loadError.value = pagination.error.value?.message ?? null
    if (!pagination.error.value) rememberSnapshot()
  }

  let searchTimer: ReturnType<typeof setTimeout> | null = null
  function syncFiltersToRoute(): void {
    const query = { ...route.query }
    const keyword = search.value.trim()
    if (keyword) query.q = keyword
    else delete query.q
    if (directionFilter.value !== 'all') query.direction = directionFilter.value
    else delete query.direction
    if (includeDeleted.value) query.deleted = '1'
    else delete query.deleted
    if (onlyMine.value) query.mine = '1'
    else delete query.mine
    void router.replace({ query })
  }

  function reloadFromFirstPage(): void {
    requestGeneration += 1
    syncFiltersToRoute()
    pagination.reset()
    templates.value = []
    directions.value = [...(directionCatalogs.get(directionCatalogKey.value) ?? [])]
    filterPending.value = true
    if (searchTimer) clearTimeout(searchTimer)
    searchTimer = setTimeout(() => {
      filterPending.value = false
      if (canOrganize.value) void load()
    }, 250)
  }

  watch([includeDeleted, onlyMine, directionFilter, search, () => user.value?.userId], reloadFromFirstPage)

  onMounted(() => {
    if (canOrganize.value) void load()
  })

  onBeforeUnmount(() => {
    requestGeneration += 1
    if (searchTimer) clearTimeout(searchTimer)
    pagination.reset()
  })

  function toggleOnlyMine(): void {
    onlyMine.value = !onlyMine.value
  }

  function visibilityLabel(visibility?: string): string {
    return visibility === 'Shared' ? translate("common.label.share") : translate("administration.label.private")
  }

  function setCreateOpen(value: boolean) {
    if (value && !canOrganize.value) return
    createOpen.value = value
    if (!value && route.path === '/admin/challenges/new')
      void navigateTo('/admin/challenges', { replace: true })
  }

  async function templateCreated(templateId: string) {
    createOpen.value = false
    await navigateTo(`/admin/challenges/${templateId}`)
  }

  const AdminDateTime = markRaw(AdminDateTimeComponent)

  const AdminGameModeBadge = markRaw(AdminGameModeBadgeComponent)

  const ChallengeTemplateCreateDialog = markRaw(ChallengeTemplateCreateDialogComponent)

  return {
      Filter,
      Plus,
      UserRound,
      canOrganize,
      templates,
      filteredTemplates,
      search,
      directionFilter,
      directionOptions,
      loading,
      loadError,
      includeDeleted,
      onlyMine,
      toggleOnlyMine,
      createOpen,
      setCreateOpen,
      templateCreated,
      visibilityLabel,
      AdminDateTime,
      AdminGameModeBadge,
      ChallengeTemplateCreateDialog,
      page: pagination.page,
      pageCount: pagination.pageCount,
      total: pagination.total,
      pageLimit: pagination.limit,
      pageLoading: loading,
      loadPage,
      setPageSize,
    }
}

export type AdminChallengesIndexPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useAdminChallengesIndexPage>>>
