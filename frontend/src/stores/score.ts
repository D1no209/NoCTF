import { defineStore } from 'pinia'
import { ref } from 'vue'
import type { NoCtfapiEndpointsCompetitionsLeaderboardEntryDto } from '@/api/generated/types.gen'

export const useScoreStore = defineStore('score', () => {
  const myTeamScore = ref<number | null>(null)
  const myTeamName = ref<string | null>(null)
  const competitionId = ref<string | null>(null)
  const teamId = ref<string | null>(null)
  const leaderboardEntries = ref<NoCtfapiEndpointsCompetitionsLeaderboardEntryDto[]>([])

  function setCurrentTeamScore(nextCompetitionId: string | null, nextTeamId: string | null, nextTeamName: string | null, score: number | null) {
    competitionId.value = nextCompetitionId
    teamId.value = nextTeamId
    myTeamName.value = nextTeamName
    myTeamScore.value = score
  }

  function updateFromLeaderboard(
    entries: NoCtfapiEndpointsCompetitionsLeaderboardEntryDto[],
    nextTeamId?: string | null,
    nextCompetitionId?: string | null,
  ) {
    competitionId.value = nextCompetitionId ?? competitionId.value
    teamId.value = nextTeamId ?? null
    leaderboardEntries.value = entries

    if (nextTeamId) {
      const myEntry = entries.find((e) => e.teamId === nextTeamId)
      if (myEntry) {
        myTeamScore.value = myEntry.totalScore ?? null
        myTeamName.value = myEntry.teamName ?? null
        return
      }
    }

    myTeamScore.value = null
  }

  function reset() {
    myTeamScore.value = null
    myTeamName.value = null
    competitionId.value = null
    teamId.value = null
    leaderboardEntries.value = []
  }

  return {
    myTeamScore,
    myTeamName,
    competitionId,
    teamId,
    leaderboardEntries,
    setCurrentTeamScore,
    updateFromLeaderboard,
    reset,
  }
})
