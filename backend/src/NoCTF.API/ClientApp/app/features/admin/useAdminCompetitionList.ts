import { ref } from 'vue'
import type { Ref } from 'vue'
import type { adminListCompetitionChallenges, adminListTeams } from '../../api'
import { useOffsetPagination } from '../../composables/useOffsetPagination'

type ChallengeListFetcher = typeof adminListCompetitionChallenges
type TeamListFetcher = typeof adminListTeams

export interface AdminCompetitionReferenceFetchers {
  listChallenges: ChallengeListFetcher
  listTeams: TeamListFetcher
}

export interface AdminCompetitionListReferenceOptions {
  /** Challenge picker options for the list filters. */
  challengeOptions: Ref<{ id: string, title: string }[]>
  teamOptions: Ref<{ id: string, name: string }[]>
  challengeTitle: (id?: string | null) => string
  teamName: (id?: string | null) => string
  loadRefs: () => Promise<void>
}

/** Challenge/team pickers shared by the competition administration lists. */
export function useAdminCompetitionReferences(
  competitionId: string,
  fetchers: AdminCompetitionReferenceFetchers,
): AdminCompetitionListReferenceOptions {
  const challengeOptions = ref<{ id: string, title: string }[]>([])
  const teamOptions = ref<{ id: string, name: string }[]>([])
  const challengeTitle = (id?: string | null) => challengeOptions.value.find(c => c.id === id)?.title ?? id ?? '-'
  const teamName = (id?: string | null) => teamOptions.value.find(t => t.id === id)?.name ?? id ?? '-'
  async function loadRefs(): Promise<void> {
    const [challenges, teams] = await Promise.all([
      fetchers.listChallenges({ path: { competitionId }, query: { includeDeleted: false } }),
      fetchers.listTeams({ path: { competitionId }, query: { keyword: null, offset: 0, limit: 200, desc: false } }),
    ])
    challengeOptions.value = (challenges.data?.items ?? []).map(c => ({ id: c.id!, title: c.title ?? '' }))
    teamOptions.value = (teams.data?.items ?? []).map(t => ({ id: t.id!, name: t.name ?? '' }))
  }
  return { challengeOptions, teamOptions, challengeTitle, teamName, loadRefs }
}

export interface AdminCompetitionListFilterOptions<F> {
  /** Draft filter snapshot captured when the reader applies the filters. */
  readFilters: () => F
  /** Runs after the first page of the newly applied filters was requested. */
  onApplied?: () => void
}

/**
 * Numbered offset pagination shared by the competition administration lists:
 * the applied filter snapshot, the first-page reset and the standard view
 * bindings. The page fetcher and the filter controls stay in the controller.
 */
export function useAdminCompetitionFilteredList<T, F extends Record<string, string>>(
  fetchPage: (
    filters: F,
    request: { offset: number, limit: number, desc: boolean },
  ) => Promise<{ items: T[], total: number }>,
  options: AdminCompetitionListFilterOptions<F>,
) {
  const appliedFilters = ref(options.readFilters())
  const pagination = useOffsetPagination<T>(
    ({ offset, limit, desc }) => fetchPage(appliedFilters.value, { offset, limit, desc }),
    { initialPageSize: 10, initialDesc: true },
  )
  const { items, loading, error: listError, initialized } = pagination
  async function applyFilters(): Promise<void> {
    appliedFilters.value = options.readFilters()
    pagination.reset()
    await pagination.loadPage(1)
    options.onApplied?.()
  }
  const listBindings = {
    items,
    loading,
    listError,
    initialized,
    page: pagination.page,
    pageCount: pagination.pageCount,
    total: pagination.total,
    pageLimit: pagination.limit,
    loadPage: pagination.loadPage,
    setPageSize: pagination.setPageSize,
  }
  return { pagination, appliedFilters, applyFilters, listBindings, items, loading, listError, initialized }
}
