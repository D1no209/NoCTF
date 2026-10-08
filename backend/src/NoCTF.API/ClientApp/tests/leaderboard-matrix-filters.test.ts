import { afterEach, describe, expect, test } from 'bun:test'
import { effectScope, nextTick, shallowRef } from 'vue'
import type { EffectScope } from 'vue'
import type { NoCtfapiEndpointsCompetitionsScoreboardTeamResponse } from '../app/api'
import type { ScoreboardChallengeColumnGroup } from '../app/utils/scoreboard'
import { useLeaderboardMatrixFilters } from '../app/features/leaderboard/useLeaderboardMatrixFilters'

const scopes: EffectScope[] = []
afterEach(() => { scopes.splice(0).forEach(scope => scope.stop()) })

function fixture() {
  const groups = shallowRef<ScoreboardChallengeColumnGroup[]>([
    { competitionChallengeId: 'headers', challenge: { id: 'headers', title: 'Orbiting Headers', direction: 'Web' }, columns: [{ index: 2 }, { index: 9 }] },
    { competitionChallengeId: 'cookies', challenge: { id: 'cookies', title: 'Midnight Cookies', direction: ' web ' }, columns: [{ index: 4 }] },
    { competitionChallengeId: 'memory', challenge: { id: 'memory', title: 'Memory Garden', direction: 'Pwn' }, columns: [{ index: 7 }] },
    { competitionChallengeId: 'unknown', challenge: null, columns: [{ index: 12 }] },
  ])
  const teams = shallowRef<NoCtfapiEndpointsCompetitionsScoreboardTeamResponse[]>(Array.from({ length: 60 }, (_, index) => ({
    teamId: `team-${index + 1}`, teamName: index >= 58 ? 'Aurora' : `Team ${index + 1}`,
    trackKey: index % 2 ? 'open' : 'student', rank: index + 1, totalScore: 600 - index,
    slots: [{ columnIndex: 2, netPoints: 125 }],
  })))
  const scope = effectScope()
  scopes.push(scope)
  const filters = scope.run(() => useLeaderboardMatrixFilters(groups, teams, team => team.teamName ?? ''))!
  return { groups, teams, filters }
}

describe('leaderboard matrix filtering', () => {
  test('combines direction, title and challenge selection while preserving round column indices', () => {
    const { groups, filters } = fixture()
    expect(filters.availableDirections.value.map(direction => direction.key)).toEqual(['web', 'pwn', '__uncategorized__'])
    filters.selectedDirectionKey.value = 'web'
    expect(filters.filteredColumnGroups.value.map(group => group.competitionChallengeId)).toEqual(['headers', 'cookies'])
    filters.challengeSearch.value = '  HEADERS  '
    filters.selectedChallengeId.value = 'headers'
    expect(filters.filteredColumnGroups.value).toEqual([groups.value[0]!])
    expect(filters.filteredColumnGroups.value[0]).toBe(groups.value[0])
    expect(filters.filteredColumnGroups.value[0]!.columns.map(column => column.index)).toEqual([2, 9])
    filters.selectedDirectionKey.value = 'pwn'
    expect(filters.filteredColumnGroups.value).toEqual([])
    expect(groups.value.map(group => group.competitionChallengeId)).toEqual(['headers', 'cookies', 'memory', 'unknown'])
  })

  test('searches all authorized teams before display limits without altering ranks, scores or columns', () => {
    const { groups, teams, filters } = fixture()
    filters.teamSearch.value = '  AuRoRa  '
    expect(filters.filteredTeams.value.map(team => team.teamId)).toEqual(['team-59', 'team-60'])
    expect(filters.filteredTeams.value[0]).toBe(teams.value[58])
    expect(filters.filteredTeams.value.map(team => [team.rank, team.totalScore])).toEqual([[59, 542], [60, 541]])
    expect(filters.filteredColumnGroups.value).toEqual(groups.value)
    teams.value = teams.value.filter(team => team.trackKey === 'student')
    expect(filters.filteredTeams.value.map(team => team.teamId)).toEqual(['team-59'])
    filters.teamSearch.value = 'not present'
    expect(filters.filteredTeams.value).toEqual([])
  })

  test('clears incompatible challenge selections as direction, title search or catalog changes', async () => {
    const { groups, filters } = fixture()
    filters.selectedChallengeId.value = 'headers'
    filters.selectedDirectionKey.value = 'pwn'
    await nextTick()
    expect(filters.selectedChallengeId.value).toBe(filters.allChallengesKey)
    expect(filters.filteredColumnGroups.value.map(group => group.competitionChallengeId)).toEqual(['memory'])
    filters.selectedDirectionKey.value = 'web'
    filters.selectedChallengeId.value = 'headers'
    filters.challengeSearch.value = 'cookies'
    await nextTick()
    expect(filters.selectedChallengeId.value).toBe(filters.allChallengesKey)
    expect(filters.filteredColumnGroups.value.map(group => group.competitionChallengeId)).toEqual(['cookies'])
    filters.challengeSearch.value = ''
    filters.selectedChallengeId.value = 'headers'
    groups.value = [groups.value[2]!]
    await nextTick()
    expect(filters.selectedDirectionKey.value).toBe(filters.allDirectionsKey)
    expect(filters.selectedChallengeId.value).toBe(filters.allChallengesKey)
    expect(filters.filteredColumnGroups.value).toEqual(groups.value)
  })

  test('supports uncategorized columns and resets every matrix filter', () => {
    const { groups, teams, filters } = fixture()
    filters.selectedDirectionKey.value = '__uncategorized__'
    expect(filters.filteredColumnGroups.value.map(group => group.competitionChallengeId)).toEqual(['unknown'])
    filters.selectedChallengeId.value = 'unknown'
    filters.teamSearch.value = 'Aurora'
    filters.challengeSearch.value = 'missing'
    expect(filters.hasMatrixFilters.value).toBeTrue()
    expect(filters.filteredColumnGroups.value).toEqual([])
    filters.clearMatrixFilters()
    expect(filters.hasMatrixFilters.value).toBeFalse()
    expect(filters.filteredTeams.value).toBe(teams.value)
    expect(filters.filteredColumnGroups.value).toEqual(groups.value)
  })
})
