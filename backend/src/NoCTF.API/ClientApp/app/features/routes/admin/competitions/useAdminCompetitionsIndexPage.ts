import { markRaw } from 'vue'

import { Plus } from '@lucide/vue'
import { adminListCompetitions } from '../../../../api'
import type { NoCtfapiEndpointsCompetitionsCompetitionResponse } from '../../../../api'
import type { CompetitionAdminRole } from '../../../../lib/admin-competition'
import CompetitionStatusBadgeComponent from '../../../admin/CompetitionStatusBadge.vue'
import GameModeBadgeComponent from '../../../admin/GameModeBadge.vue'

/** Owns state, effects and commands for AdminCompetitionsIndexPage. */
export function useAdminCompetitionsIndexPage() {
  const route = useRoute()

  const { canOrganize } = useAuth()

  const items = ref<NoCtfapiEndpointsCompetitionsCompetitionResponse[]>([])

  const roles = ref<Record<string, CompetitionAdminRole>>({})

  const loading = ref(true)

  const error = ref<string | null>(null)

  const includeDeleted = ref(route.query.includeDeleted === 'true')

  const RoleLabel: Record<CompetitionAdminRole, string> = {
    Owner: translate("ui.owner"),
    Manager: translate("ui.administrator"),
    Judge: translate("ui.judge"),
    Observer: translate("ui.observer"),
  }

  function adminRole(competition: NoCtfapiEndpointsCompetitionsCompetitionResponse): CompetitionAdminRole {
    return competition.administrationRole ?? 'Observer'
  }

  async function load() {
    loading.value = true
    error.value = null
    const { data, error: e } = await adminListCompetitions({
      query: { includeDeleted: includeDeleted.value },
    })
    if (e || !data) {
      error.value = parseApiError(e).message
      loading.value = false
      return
    }
    items.value = data.items ?? []
    roles.value = Object.fromEntries(
      items.value.flatMap(competition => competition.id
        ? [[competition.id, adminRole(competition)] as const]
        : []),
    )
    loading.value = false
  }

  watch(includeDeleted, () => {
    void load()
  })

  onMounted(load)

  const CompetitionStatusBadge = markRaw(CompetitionStatusBadgeComponent)

  const GameModeBadge = markRaw(GameModeBadgeComponent)

  return {
      Plus,
      canOrganize,
      items,
      roles,
      loading,
      error,
      includeDeleted,
      RoleLabel,
      CompetitionStatusBadge,
      GameModeBadge
    }
}

export type AdminCompetitionsIndexPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useAdminCompetitionsIndexPage>>>
