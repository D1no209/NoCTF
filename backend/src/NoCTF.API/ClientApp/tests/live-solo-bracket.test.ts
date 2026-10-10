import { describe, expect, test } from 'bun:test'
import { hasSeededBracket, selectableTeams, reorderSeed, bracketRows } from '../app/features/live-solo/bracket-state'
describe('LiveSolo schedule state', () => {
  test('only approved non-banned teams without an active match are selectable', () => {
    const teams = [{ id: 'one', registrationStatus: 'Approved' as const }, { id: 'two', registrationStatus: 'Pending' as const },
      { id: 'three', registrationStatus: 'Approved' as const, isBanned: true }, { id: 'four', registrationStatus: 'Approved' as const }]
    expect(selectableTeams(teams, { matches: [{ match: { state: 'Paused', leftTeamId: 'one' } }] }).map(x => x.id)).toEqual(['four'])
    expect(selectableTeams(teams, { matches: [{ match: { state: 'Completed', leftTeamId: 'one' } }] }).map(x => x.id)).toEqual(['one', 'four'])
  })
  test('a two-team seeded tournament is distinct from manual seed slots without seed numbers', () => {
    expect(hasSeededBracket({ matches: [{ sources: [{ source: 'Seed', seed: 1 }] }] })).toBe(true)
    expect(hasSeededBracket({ matches: [{ sources: [{ source: 'Seed', seed: null }] }] })).toBe(false)
  })
  test('seed reorder is deterministic and preserves the draft input', () => {
    const ids = ['a', 'b', 'c']; expect(reorderSeed(ids, 2, -1)).toEqual(['a', 'c', 'b']); expect(ids).toEqual(['a', 'b', 'c'])
    expect(reorderSeed(ids, 0, -1)).toEqual(ids)
  })
  test('unknown match sources remain explicit instead of invented team identities', () => {
    const rows = bracketRows({ matches: [{ match: { id: 'future', state: 'AwaitingOpponents' }, lane: 'Losers', stage: 2 }] })
    expect(rows[0]?.stateKey).toBe('liveSolo.state.awaitingOpponents'); expect(rows[0]?.laneKey).toBe('liveSolo.settings.losers')
  })
})
