

import { listCompetitionTeamsEndpoint } from '../../../../../api'
import type { NoCtfapiEndpointsTeamsTeamResponse } from '../../../../../api'

/** Owns state, effects and commands for CompetitionsByIdTeamsIndexPage. */
export function useCompetitionsByIdTeamsIndexPage() {
  const route = useRoute()

  const competitionId = route.params.id as string

  const teams = ref<NoCtfapiEndpointsTeamsTeamResponse[]>([])

  const loading = ref(true)

  const error = ref<string | null>(null)

  const teamDisplayNames = computed(() => buildTeamDisplayNames(teams.value))

  onMounted(async () => {
    const { data, error: err } = await listCompetitionTeamsEndpoint({ path: { competitionId } })
    loading.value = false
    if (err || !data) {
      error.value = parseApiError(err, translate("ui.failedToLoadTeamList")).message
      return
    }
    teams.value = data.items ?? []
  })

  return {
      competitionId,
      teams,
      loading,
      error,
      teamDisplayNames
    }
}

export type CompetitionsByIdTeamsIndexPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useCompetitionsByIdTeamsIndexPage>>>
