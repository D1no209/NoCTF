import { message as describeMessage } from '../../../../../utils/i18n'
import type { UiMessage } from '../../../../../utils/i18n'
import { markRaw } from 'vue'

import { getTeamEndpoint } from '../../../../../api'
import type { NoCtfapiEndpointsTeamsTeamResponse } from '../../../../../api'
import TeamMembersComponent from '../../../../teams/TeamMembers.vue'

/** Owns state, effects and commands for CompetitionsByIdTeamsByTeamIdPage. */
export function useCompetitionsByIdTeamsByTeamIdPage() {
  const route = useRoute()

  const competitionId = route.params.id as string

  const teamId = route.params.teamId as string

  const team = ref<NoCtfapiEndpointsTeamsTeamResponse | null>(null)

  const loading = ref(true)

  const error = ref<UiMessage | null>(null)

  onMounted(async () => {
    const { data, error: err } = await getTeamEndpoint({ path: { competitionId, teamId } })
    loading.value = false
    if (err || !data) {
      error.value = parseApiError(err, describeMessage("competitions.competitionsBy.error.loadTeamInformationFailed")).displayMessage
      return
    }
    team.value = data
  })

  const TeamMembers = markRaw(TeamMembersComponent)

  return {
      competitionId,
      team,
      loading,
      error,
      TeamMembers
    }
}

export type CompetitionsByIdTeamsByTeamIdPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useCompetitionsByIdTeamsByTeamIdPage>>>
