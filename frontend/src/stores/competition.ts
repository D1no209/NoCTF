import { defineStore } from 'pinia'
import { ref } from 'vue'
import type { NoCtfapiEndpointsCompetitionsCompetitionResponse } from '@/api/generated/types.gen'

export const useCompetitionStore = defineStore('competition', () => {
  const competitions = ref<NoCtfapiEndpointsCompetitionsCompetitionResponse[]>([])
  const currentCompetition = ref<NoCtfapiEndpointsCompetitionsCompetitionResponse | null>(null)

  function setCurrentCompetition(competition: NoCtfapiEndpointsCompetitionsCompetitionResponse | null) {
    currentCompetition.value = competition
  }

  function setCompetitions(list: NoCtfapiEndpointsCompetitionsCompetitionResponse[]) {
    competitions.value = list
  }

  return { competitions, currentCompetition, setCurrentCompetition, setCompetitions }
})
