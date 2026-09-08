import { markRaw } from 'vue'

import CompetitionChallengeDetailComponent from '../../../../challenges/CompetitionChallengeDetail.vue'
import CompetitionParticipantWorkspaceComponent from '../../../../competition/CompetitionParticipantWorkspace.vue'

/** Owns state, effects and commands for CompetitionsByIdChallengesIndexPage. */
export function useCompetitionsByIdChallengesIndexPage() {
  const route = useRoute()

  const router = useRouter()

  const competitionId = route.params.id as string

  const selectedChallengeId = computed(() =>
    typeof route.query.challenge === 'string' ? route.query.challenge : null,
  )

  async function selectChallenge(challengeId: string): Promise<void> {
    if (selectedChallengeId.value === challengeId) return
    await router.replace({
      path: `/competitions/${competitionId}/challenges`,
      query: { ...route.query, challenge: challengeId },
    })
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
