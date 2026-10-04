
import { api } from '../../../../../lib/api'
import { message as describeMessage } from '../../../../../utils/i18n'



import type { NoCTFAPIEndpointsTeamsTeamResponse } from '../../../../../api/models'
import { computed, ref } from 'vue'
import { useOffsetPagination } from '../../../../../composables/useOffsetPagination'
import { parseApiError } from '../../../../../utils/api-error'
import { buildTeamDisplayNames } from '../../../../../utils/team-display'

/** Owns state, effects and commands for CompetitionsByIdTeamsIndexPage. */
export function useCompetitionsByIdTeamsIndexPage() {
  const route = useRoute()

  const competitionId = route.params.id as string

  const allTeams = ref<NoCTFAPIEndpointsTeamsTeamResponse[] | null>(null)
  const pagination = useOffsetPagination<NoCTFAPIEndpointsTeamsTeamResponse>(async ({ offset, limit }) => {
    if (allTeams.value === null) {
      let requestError: unknown;
      const data = await api.api.v1.competitions.byCompetitionId(competitionId).teams.get().catch(cause => { requestError = cause; return undefined });
      if (requestError || !data)
        throw parseApiError(requestError, describeMessage('competitions.competitionsBy.error.loadTeamListFailed'))
      allTeams.value = data.items ?? []
    }
    return { items: allTeams.value.slice(offset, offset + limit), total: allTeams.value.length }
  })

  const loading = computed(() => pagination.loading.value
    || !pagination.initialized.value && pagination.error.value === null)
  const error = computed(() => pagination.error.value?.message ?? null)
  const teamDisplayNames = computed(() => buildTeamDisplayNames(allTeams.value ?? []))

  onMounted(() => pagination.loadPage(1))
  onBeforeUnmount(pagination.reset)

  return {
      competitionId,
      teams: pagination.items,
      loading,
      error,
      teamDisplayNames,
      page: pagination.page,
      pageLimit: pagination.limit,
      pageCount: pagination.pageCount,
      total: pagination.total,
      initialized: pagination.initialized,
      loadPage: pagination.loadPage,
      setPageSize: pagination.setPageSize,
    }
}

export type CompetitionsByIdTeamsIndexPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useCompetitionsByIdTeamsIndexPage>>>
