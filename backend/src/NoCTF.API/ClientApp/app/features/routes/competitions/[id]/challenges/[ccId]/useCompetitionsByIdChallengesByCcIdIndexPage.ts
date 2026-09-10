

/** Owns state, effects and commands for CompetitionsByIdChallengesByCcIdIndexPage. */
export function useCompetitionsByIdChallengesByCcIdIndexPage() {
  const route = useRoute()

  const competitionId = route.params.id as string

  const competitionChallengeId = route.params.ccId as string

  async function initialize() {
    await navigateTo({
      path: `/competitions/${competitionId}/challenges`,
      query: { challenge: competitionChallengeId },
    }, { replace: true })
  }

  return {
      initialize,

    }
}

export type CompetitionsByIdChallengesByCcIdIndexPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useCompetitionsByIdChallengesByCcIdIndexPage>>>
