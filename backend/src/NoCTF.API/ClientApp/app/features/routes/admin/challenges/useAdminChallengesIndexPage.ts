import { markRaw } from 'vue'

import { Plus } from '@lucide/vue'
import { adminChallengeBankListTemplates } from '../../../../api'
import type { NoCtfapiEndpointsAdministrationChallengeBankChallengeTemplateResponse } from '../../../../api'
import AdminDateTimeComponent from '../../../admin/AdminDateTime.vue'
import AdminGameModeBadgeComponent from '../../../admin/AdminGameModeBadge.vue'

/** Owns state, effects and commands for AdminChallengesIndexPage. */
export function useAdminChallengesIndexPage() {
  const { canOrganize } = useAuth()

  const templates = ref<NoCtfapiEndpointsAdministrationChallengeBankChallengeTemplateResponse[]>([])

  const loading = ref(false)

  const loadError = ref<string | null>(null)

  const includeDeleted = ref(false)

  async function load(): Promise<void> {
    loading.value = true
    loadError.value = null
    const { data, error } = await adminChallengeBankListTemplates({
      query: { includeDeleted: includeDeleted.value },
    })
    if (error || !data) {
      loadError.value = parseApiError(error).message
    }
    else {
      templates.value = data.items ?? []
    }
    loading.value = false
  }

  watch(includeDeleted, () => {
    void load()
  })

  onMounted(() => {
    if (canOrganize.value) void load()
  })

  function visibilityLabel(visibility?: string): string {
    return visibility === 'Shared' ? translate("ui.share") : translate("ui.private")
  }

  const AdminDateTime = markRaw(AdminDateTimeComponent)

  const AdminGameModeBadge = markRaw(AdminGameModeBadgeComponent)

  return {
      Plus,
      canOrganize,
      templates,
      loading,
      loadError,
      includeDeleted,
      visibilityLabel,
      AdminDateTime,
      AdminGameModeBadge
    }
}

export type AdminChallengesIndexPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useAdminChallengesIndexPage>>>
