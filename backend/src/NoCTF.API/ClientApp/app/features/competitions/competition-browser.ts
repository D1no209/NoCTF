import type { NoCtfapiEndpointsCompetitionsCompetitionResponse as Competition } from '~/api'

export type CompetitionGroup = 'running' | 'upcoming' | 'finished' | 'deleted'
export interface CompetitionSidebarOption { value: string; label: string; competition: Competition }
export const competitionGroups: CompetitionGroup[] = ['running', 'upcoming', 'finished']

export function competitionGroup(competition: Competition, includeManagement = false): CompetitionGroup | null {
  if (competition.deletedAt) return includeManagement ? 'deleted' : null
  if (competition.status === 'Draft') return includeManagement || competition.administrationRole != null ? 'upcoming' : null
  if (competition.status === 'Running' || competition.status === 'Paused') return 'running'
  if (competition.status === 'Published' || competition.status === 'Visible') return 'upcoming'
  return competition.status === 'Finished' ? 'finished' : null
}

export function resolveCompetitionBrowser(items: Competition[], requestedId: string | null, requestedGroup: string | null, includeManagement = false) {
  const availableGroups = includeManagement ? [...competitionGroups, 'deleted' as const] : competitionGroups
  const groups = {
    running: items.filter(c => c.id && competitionGroup(c, includeManagement) === 'running').sort((a, b) => (a.endTime ?? '').localeCompare(b.endTime ?? '')),
    upcoming: items.filter(c => c.id && competitionGroup(c, includeManagement) === 'upcoming').sort((a, b) => (a.startTime ?? '').localeCompare(b.startTime ?? '')),
    finished: items.filter(c => c.id && competitionGroup(c, includeManagement) === 'finished').sort((a, b) => (b.endTime ?? '').localeCompare(a.endTime ?? '')),
    deleted: items.filter(c => c.id && competitionGroup(c, includeManagement) === 'deleted').sort((a, b) => (b.deletedAt ?? '').localeCompare(a.deletedAt ?? '')),
  }
  const requested = items.find(c => c.id === requestedId)
  const group: CompetitionGroup = (requested && competitionGroup(requested, includeManagement))
    || (availableGroups.includes(requestedGroup as CompetitionGroup) ? requestedGroup as CompetitionGroup : availableGroups.find(key => groups[key].length) ?? 'running')
  const selected = requestedId ? groups[group].find(c => c.id === requestedId) ?? null : groups[group][0] ?? null
  return { groups, group, selected, items: groups[group], missing: Boolean(requestedId && !selected) }
}
