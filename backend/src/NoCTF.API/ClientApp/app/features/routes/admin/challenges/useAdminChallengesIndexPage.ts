import { markRaw } from 'vue'

import { Plus } from '@lucide/vue'
import { adminChallengeBankListTemplates } from '../../../../api'
import type { NoCtfapiEndpointsAdministrationChallengeBankChallengeTemplateResponse } from '../../../../api'
import AdminDateTimeComponent from '../../../admin/AdminDateTime.vue'
import AdminGameModeBadgeComponent from '../../../admin/AdminGameModeBadge.vue'
import ChallengeTemplateCreateDialogComponent from '../../../admin/ChallengeTemplateCreateDialog.vue'
import { directionKey, directionLabel } from '../../../../utils/directions'
import { useOffsetPagination } from '../../../../composables/useOffsetPagination'

type ChallengeTemplate = NoCtfapiEndpointsAdministrationChallengeBankChallengeTemplateResponse

let lastIncludeDeleted = false

/** Owns state, effects and commands for AdminChallengesIndexPage. */
export function useAdminChallengesIndexPage() {
  const route = useRoute()

  const { canOrganize } = useAuth()

  const createOpen = ref(route.path === '/admin/challenges/new')

  const includeDeleted = ref(lastIncludeDeleted)

  const templates = ref<ChallengeTemplate[]>([])

  const directions = ref<string[]>([])

  const loadError = ref<string | null>(null)

  const search = ref('')

  const directionFilter = ref('all')

  const directionOptions = computed(() => directions.value
    .map(value => ({ value: directionKey(value), label: directionLabel(value) }))
    .filter(option => option.value)
    .sort((left, right) => left.label.localeCompare(right.label)))

  const filteredTemplates = computed(() => templates.value)

  const pagination = useOffsetPagination<ChallengeTemplate>(async ({ offset, limit, desc }) => {
    const { data, error } = await adminChallengeBankListTemplates({
      query: {
        includeDeleted: includeDeleted.value,
        keyword: search.value.trim() || null,
        direction: directionFilter.value === 'all' ? null : directionFilter.value,
        offset,
        limit,
        desc,
      },
    })
    if (error || !data) throw error ?? new Error('Failed to load challenge templates.')
    templates.value = data.items ?? []
    directions.value = data.directions ?? []
    return { items: templates.value, total: data.total ?? 0 }
  }, { initialDesc: true })

  const loading = pagination.loading

  async function load(): Promise<void> {
    loadError.value = null
    await pagination.loadPage(pagination.page.value)
    loadError.value = pagination.error.value?.message ?? null
  }

  let searchTimer: ReturnType<typeof setTimeout> | null = null
  function reloadFromFirstPage(): void {
    lastIncludeDeleted = includeDeleted.value
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
    pagination.reset()
  })

  function visibilityLabel(visibility?: string): string {
    return visibility === 'Shared' ? translate("ui.share") : translate("ui.private")
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
      loadPage: pagination.loadPage,
      setPageSize: pagination.setPageSize,
    }
}

export type AdminChallengesIndexPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useAdminChallengesIndexPage>>>
