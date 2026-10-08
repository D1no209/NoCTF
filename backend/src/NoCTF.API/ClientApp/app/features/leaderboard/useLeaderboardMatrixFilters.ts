import { computed, ref, watch } from 'vue'
import type { Ref } from 'vue'
import type { NoCtfapiEndpointsCompetitionsScoreboardTeamResponse } from '../../api'
import type { ScoreboardChallengeColumnGroup } from '../../utils/scoreboard'
import { directionKey, directionLabel } from '../../utils/directions'

/** Filters presentation rows and columns without changing authoritative ranks or slot indices. */
export function useLeaderboardMatrixFilters(
  groups: Readonly<Ref<ScoreboardChallengeColumnGroup[]>>,
  teams: Readonly<Ref<NoCtfapiEndpointsCompetitionsScoreboardTeamResponse[]>>,
  displayTeamName: (team: NoCtfapiEndpointsCompetitionsScoreboardTeamResponse) => string,
) {
  const allDirectionsKey = '__all_directions__'
  const allChallengesKey = '__all_challenges__'
  const uncategorizedKey = '__uncategorized__'
  const selectedDirectionKey = ref(allDirectionsKey)
  const selectedChallengeId = ref(allChallengesKey)
  const teamSearch = ref('')
  const challengeSearch = ref('')

  const groupDirectionKey = (group: ScoreboardChallengeColumnGroup) =>
    directionKey(group.challenge?.direction) || uncategorizedKey

  const availableDirections = computed(() => [...new Map(groups.value.map(group => [
    groupDirectionKey(group),
    { key: groupDirectionKey(group), label: directionLabel(group.challenge?.direction) },
  ])).values()])

  const availableChallenges = computed(() => {
    const query = challengeSearch.value.trim().toLowerCase()
    return groups.value.filter(group => (
      selectedDirectionKey.value === allDirectionsKey || groupDirectionKey(group) === selectedDirectionKey.value
    ) && (!query || (group.challenge?.title ?? '').toLowerCase().includes(query)))
  })

  watch(availableDirections, (directions) => {
    if (selectedDirectionKey.value !== allDirectionsKey && !directions.some(direction => direction.key === selectedDirectionKey.value))
      selectedDirectionKey.value = allDirectionsKey
  })

  watch(availableChallenges, (challenges) => {
    if (selectedChallengeId.value !== allChallengesKey && !challenges.some(group => group.competitionChallengeId === selectedChallengeId.value))
      selectedChallengeId.value = allChallengesKey
  })

  const filteredColumnGroups = computed(() => selectedChallengeId.value === allChallengesKey
    ? availableChallenges.value
    : availableChallenges.value.filter(group => group.competitionChallengeId === selectedChallengeId.value))

  const filteredTeams = computed(() => {
    const query = teamSearch.value.trim().toLowerCase()
    return query ? teams.value.filter(team => displayTeamName(team).toLowerCase().includes(query)) : teams.value
  })

  const hasMatrixFilters = computed(() => Boolean(
    teamSearch.value || challengeSearch.value
    || selectedDirectionKey.value !== allDirectionsKey || selectedChallengeId.value !== allChallengesKey,
  ))

  function clearMatrixFilters() {
    teamSearch.value = ''
    challengeSearch.value = ''
    selectedDirectionKey.value = allDirectionsKey
    selectedChallengeId.value = allChallengesKey
  }

  return {
    allDirectionsKey, allChallengesKey, selectedDirectionKey, selectedChallengeId,
    teamSearch, challengeSearch, availableDirections, availableChallenges,
    filteredColumnGroups, filteredTeams, hasMatrixFilters, clearMatrixFilters,
  }
}
