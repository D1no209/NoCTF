import { markRaw } from 'vue'

import CompetitionChallengeDetailComponent from '../../../../challenges/CompetitionChallengeDetail.vue'
import CompetitionParticipantWorkspaceComponent from '../../../../competition/CompetitionParticipantWorkspace.vue'
import { competitionChallengePath } from '../../../../../utils/app-routes'

/** Owns state, effects and commands for CompetitionsByIdChallengesIndexPage. */
export function useCompetitionsByIdChallengesIndexPage() {
  const route = useRoute()
  if ('challenge' in route.query) throw createError({ statusCode: 404, statusMessage: 'Page not found' })

  const router = useRouter()

  const competitionId = route.params.id as string

  const selectedChallengeId = computed(() =>
    typeof route.params.ccId === 'string' ? route.params.ccId : null,
  )

  async function selectChallenge(challengeId: string): Promise<void> {
    if (selectedChallengeId.value === challengeId) return
    await router.replace({ path: competitionChallengePath(competitionId, challengeId), query: route.query })
  }

  const CompetitionChallengeDetail = markRaw(CompetitionChallengeDetailComponent)

  const CompetitionParticipantWorkspace = markRaw(CompetitionParticipantWorkspaceComponent)

  return {
      competitionId,
      selectedChallengeId,
      selectChallenge,
      CompetitionChallengeDetail,
      CompetitionParticipantWorkspace
    }
}

export type CompetitionsByIdChallengesIndexPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useCompetitionsByIdChallengesIndexPage>>>
