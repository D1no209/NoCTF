
import { api } from '../../../../lib/api'
import type { UiMessage } from '../../../../utils/i18n'
import { markRaw } from 'vue'

import { Plus } from '@lucide/vue'

import type { NoCTFAPIEndpointsAdministrationChallengeBankChallengeTemplateSummaryResponse } from '../../../../api/models'
import AdminDateTimeComponent from '../../../admin/AdminDateTime.vue'
import AdminGameModeBadgeComponent from '../../../admin/AdminGameModeBadge.vue'
import ChallengeTemplateCreateDialogComponent from '../../../admin/ChallengeTemplateCreateDialog.vue'
import { directionKey, directionLabel } from '../../../../utils/directions'
import { useOffsetPagination } from '../../../../composables/useOffsetPagination'

type ChallengeTemplate = NoCTFAPIEndpointsAdministrationChallengeBankChallengeTemplateSummaryResponse

let lastIncludeDeleted = false
interface ChallengeLibrarySnapshot {
  includeDeleted: boolean
  search: string
  directionFilter: string
  templates: ChallengeTemplate[]
  directions: string[]
  page: number
  limit: number
  total: number
}
let lastSnapshot: ChallengeLibrarySnapshot | null = null
const directionCatalogs = new Map<boolean, string[]>()

/** Owns state, effects and commands for AdminChallengesIndexPage. */
export function useAdminChallengesIndexPage() {
  const route = useRoute()
  const router = useRouter()

  const { canOrganize } = useAuth()

  const createOpen = ref(route.path === '/admin/challenges/new')

  const restored = lastSnapshot

  const includeDeleted = ref(route.query.deleted === '1'
    ? true
    : restored?.includeDeleted ?? lastIncludeDeleted)

  const templates = ref<ChallengeTemplate[]>([...(restored?.templates ?? [])])

  const directions = ref<string[]>([...(restored?.directions
    ?? directionCatalogs.get(includeDeleted.value)
    ?? [])])

  const loadError = ref<UiMessage | null>(null)

  const search = ref(typeof route.query.q === 'string'
    ? route.query.q
    : restored?.search ?? '')

  const directionFilter = ref(typeof route.query.direction === 'string'
    ? route.query.direction
    : restored?.directionFilter ?? 'all')

  const directionOptions = computed(() => [...new Map(directions.value
    .map(value => ({ key: directionKey(value), value, label: directionLabel(value) }))
    .filter(option => option.key)
    .map(option => [option.key, option] as const)).values()]
    .sort((left, right) => left.label.localeCompare(right.label)))

  const filteredTemplates = computed(() => templates.value)

  const pagination = useOffsetPagination<ChallengeTemplate>(async ({ offset, limit, desc }) => {
    let error: unknown;
    const data = await api.api.v1.admin.challenges.get({ queryParameters: {
        includeDeleted: includeDeleted.value,
        keyword: search.value.trim() || undefined,
        direction: directionFilter.value === 'all' ? undefined : directionFilter.value,
        offset,
        limit,
        desc,
      } }).catch(cause => { error = cause; return undefined });
    if (error || !data) throw error ?? new Error('Failed to load challenge templates.')
    templates.value = data.items ?? []
    const catalog = [...new Set([
      ...(directionCatalogs.get(includeDeleted.value) ?? []),
      ...directions.value,
      ...(data.directions ?? []),
    ])]
    directionCatalogs.set(includeDeleted.value, catalog)
    directions.value = catalog
    return { items: templates.value, total: data.total ?? 0 }
  }, { initialDesc: true })

  if (restored) {
    pagination.page.value = restored.page
    pagination.limit.value = restored.limit
    pagination.total.value = restored.total
    pagination.items.value = [...restored.templates]
    pagination.initialized.value = true
    directionCatalogs.set(restored.includeDeleted, [...restored.directions])
  }

  const loading = pagination.loading

  function rememberSnapshot(): void {
    lastIncludeDeleted = includeDeleted.value
    lastSnapshot = {
      includeDeleted: includeDeleted.value,
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
    loadError.value = null
    await pagination.loadPage(targetPage)
    loadError.value = pagination.error.value?.message ?? null
    if (!pagination.error.value) rememberSnapshot()
  }

  async function load(): Promise<void> {
    await loadPage(pagination.page.value)
  }

  async function setPageSize(value: number): Promise<void> {
    loadError.value = null
    await pagination.setPageSize(value)
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
    void router.replace({ query })
  }

  function reloadFromFirstPage(): void {
    rememberSnapshot()
    syncFiltersToRoute()
    pagination.reset()
    if (searchTimer) clearTimeout(searchTimer)
    searchTimer = setTimeout(() => { void load() }, 250)
  }

  watch([includeDeleted, directionFilter, search], reloadFromFirstPage)

  onMounted(() => {
    if (canOrganize.value) void load()
  })

  onBeforeUnmount(() => {
    if (searchTimer) clearTimeout(searchTimer)
    rememberSnapshot()
    pagination.reset()
  })

  function visibilityLabel(visibility?: string | null): string {
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
      Plus,
      canOrganize,
      templates,
      filteredTemplates,
      search,
      directionFilter,
      directionOptions,
      loading,
      loadError,
      includeDeleted,
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
      pageLoading: pagination.loading,
      loadPage,
      setPageSize,
    }
}

export type AdminChallengesIndexPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useAdminChallengesIndexPage>>>
