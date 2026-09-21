import { markRaw } from 'vue'

import CompetitionChallengeDetailComponent from '../../../../challenges/CompetitionChallengeDetail.vue'
import CompetitionParticipantWorkspaceComponent from '../../../../competition/CompetitionParticipantWorkspace.vue'
import { competitionChallengePath } from '../../../../../utils/app-routes'

/** Owns state, effects and commands for CompetitionsByIdChallengesIndexPage. */
export function useCompetitionsByIdChallengesIndexPage() {
  const route = useRoute()

  const router = useRouter()

  const competitionId = route.params.id as string

  const selectedChallengeId = computed(() =>
    typeof route.params.ccId === 'string'
      ? route.params.ccId
      : typeof route.query.challenge === 'string' ? route.query.challenge : null,
  )

  async function selectChallenge(challengeId: string): Promise<void> {
    if (selectedChallengeId.value === challengeId) return
    const { challenge: _legacyChallenge, ...query } = route.query
    await router.replace({ path: competitionChallengePath(competitionId, challengeId), query })
  }

  watch(
    () => [route.params.ccId, route.query.challenge] as const,
    ([challengeId, legacyChallengeId]) => {
      if (typeof challengeId !== 'string' && typeof legacyChallengeId === 'string') {
        const { challenge: _legacyChallenge, ...query } = route.query
        void router.replace({ path: competitionChallengePath(competitionId, legacyChallengeId), query })
      }
    },
    { immediate: true },
  )

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
