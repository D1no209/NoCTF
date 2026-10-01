

import { listCompetitionTeamsEndpoint } from '../../../../../api'
import type { NoCtfapiEndpointsTeamsTeamResponse } from '../../../../../api'
import { computed, ref } from 'vue'
import { useOffsetPagination } from '../../../../../composables/useOffsetPagination'
import { parseApiError } from '../../../../../utils/api-error'
import { translate } from '../../../../../utils/i18n'
import { buildTeamDisplayNames } from '../../../../../utils/team-display'

/** Owns state, effects and commands for CompetitionsByIdTeamsIndexPage. */
export function useCompetitionsByIdTeamsIndexPage() {
  const route = useRoute()

  const competitionId = route.params.id as string

  const allTeams = ref<NoCtfapiEndpointsTeamsTeamResponse[] | null>(null)
  const pagination = useOffsetPagination<NoCtfapiEndpointsTeamsTeamResponse>(async ({ offset, limit }) => {
    if (allTeams.value === null) {
      const { data, error: requestError } = await listCompetitionTeamsEndpoint({ path: { competitionId } })
      if (requestError || !data)
        throw parseApiError(requestError, translate('ui.failedToLoadTeamList'))
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
