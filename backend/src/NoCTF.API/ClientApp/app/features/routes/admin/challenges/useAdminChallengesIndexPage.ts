import { markRaw } from 'vue'

import { Plus } from '@lucide/vue'
import { adminChallengeBankListTemplates } from '../../../../api'
import type { NoCtfapiEndpointsAdministrationChallengeBankChallengeTemplateResponse } from '../../../../api'
import AdminDateTimeComponent from '../../../admin/AdminDateTime.vue'
import AdminGameModeBadgeComponent from '../../../admin/AdminGameModeBadge.vue'
import ChallengeTemplateCreateDialogComponent from '../../../admin/ChallengeTemplateCreateDialog.vue'
import { directionKey, directionLabel } from '../../../../utils/directions'

type ChallengeTemplate = NoCtfapiEndpointsAdministrationChallengeBankChallengeTemplateResponse

const templateListCache = new Map<boolean, ChallengeTemplate[]>()
let lastIncludeDeleted = false

/** Owns state, effects and commands for AdminChallengesIndexPage. */
export function useAdminChallengesIndexPage() {
  const route = useRoute()

  const { canOrganize } = useAuth()

  const createOpen = ref(route.path === '/admin/challenges/new')

  const includeDeleted = ref(lastIncludeDeleted)

  const templates = ref<ChallengeTemplate[]>([...(templateListCache.get(includeDeleted.value) ?? [])])

  const loading = ref(false)

  const loadError = ref<string | null>(null)

  const directionFilter = ref('all')

  const directionOptions = computed(() => [...new Map(templates.value
    .map(template => [directionKey(template.direction), directionLabel(template.direction)] as const)
    .filter(([value]) => value))]
    .map(([value, label]) => ({ value, label }))
    .sort((left, right) => left.label.localeCompare(right.label)))

  const filteredTemplates = computed(() => directionFilter.value === 'all'
    ? templates.value
    : templates.value.filter(template => directionKey(template.direction) === directionFilter.value))

  let loadGeneration = 0

  async function load(): Promise<void> {
    const generation = ++loadGeneration
    const requestedIncludeDeleted = includeDeleted.value
    loading.value = true
    loadError.value = null
    try {
      const { data, error } = await adminChallengeBankListTemplates({
        query: { includeDeleted: requestedIncludeDeleted },
      })
      if (generation !== loadGeneration) return
      if (error || !data) {
        loadError.value = parseApiError(error).message
        return
      }
      const nextTemplates = data.items ?? []
      templateListCache.set(requestedIncludeDeleted, [...nextTemplates])
      templates.value = [...nextTemplates]
    }
    catch (error) {
      if (generation === loadGeneration)
        loadError.value = parseApiError(error).message
    }
    finally {
      if (generation === loadGeneration)
        loading.value = false
    }
  }

  watch(includeDeleted, value => {
    lastIncludeDeleted = value
    loadGeneration += 1
    templates.value = [...(templateListCache.get(value) ?? [])]
    loading.value = false
    loadError.value = null
    void load()
  })

  onMounted(() => {
    if (canOrganize.value) void load()
  })

  onBeforeUnmount(() => {
    loadGeneration += 1
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
      ChallengeTemplateCreateDialog
    }
}

export type AdminChallengesIndexPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useAdminChallengesIndexPage>>>
