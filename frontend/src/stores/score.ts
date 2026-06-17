import { defineStore } from 'pinia'
import { ref } from 'vue'
import type { NoCtfapiEndpointsCompetitionsLeaderboardEntryDto } from '@/api/generated/types.gen'

export const useScoreStore = defineStore('score', () => {
  const myTeamScore = ref<number | null>(null)
  const myTeamName = ref<string | null>(null)
  const leaderboardEntries = ref<NoCtfapiEndpointsCompetitionsLeaderboardEntryDto[]>([])

  function updateFromLeaderboard(entries: NoCtfapiEndpointsCompetitionsLeaderboardEntryDto[], teamId?: string | null) {
    leaderboardEntries.value = entries
    if (teamId) {
      const myEntry = entries.find((e) => e.teamId === teamId)
      if (myEntry) {
        myTeamScore.value = myEntry.totalScore ?? null
        myTeamName.value = myEntry.teamName ?? null
        return
      }
    }
    // fallback: first entry
    if (entries.length > 0) {
      myTeamScore.value = entries[0].totalScore ?? null
      myTeamName.value = entries[0].teamName ?? null
    }
  }

  function reset() {
    myTeamScore.value = null
    myTeamName.value = null
    leaderboardEntries.value = []
  }

  return { myTeamScore, myTeamName, leaderboardEntries, updateFromLeaderboard, reset }
})
