import { defineStore } from 'pinia'
import { ref } from 'vue'
import type { NoCtfapiEndpointsAdminCompetitionSummaryDto } from '@/api/generated/types.gen'

export const useCompetitionStore = defineStore('competition', () => {
  const competitions = ref<NoCtfapiEndpointsAdminCompetitionSummaryDto[]>([])
  const currentCompetition = ref<NoCtfapiEndpointsAdminCompetitionSummaryDto | null>(null)

  function setCurrentCompetition(competition: NoCtfapiEndpointsAdminCompetitionSummaryDto | null) {
    currentCompetition.value = competition
  }

  function setCompetitions(list: NoCtfapiEndpointsAdminCompetitionSummaryDto[]) {
    competitions.value = list
  }

  return { competitions, currentCompetition, setCurrentCompetition, setCompetitions }
})
