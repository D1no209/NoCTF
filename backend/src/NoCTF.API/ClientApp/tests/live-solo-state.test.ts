import { describe, expect, test } from 'bun:test'
import { canPlayLiveSolo, canJudgeLiveSolo, retainQuestionSelection, questionFromWorkspaceRoute,
  roundRemainingSeconds, formatRoundClock } from '../app/features/live-solo/live-solo-state'
import { canEnterCompetition } from '../app/lib/competition-participation'

describe('LiveSolo participant state', () => {
  test('new releases preserve the member selection, including an earlier opened question', () => {
    expect(retainQuestionSelection('first', [{ id: 'first' }, { id: 'second' }])).toBe('first')
    expect(retainQuestionSelection(null, [])).toBeNull()
    expect(retainQuestionSelection('gone', [{ id: 'second' }])).toBe('second')
  })
  test('entity routes restore only an opened question in the current round', () => {
    const rows = [{ id: 'opened' }]
    expect(questionFromWorkspaceRoute(['rounds', 'round', 'questions', 'opened'], 'round', null, rows)).toBe('opened')
    expect(questionFromWorkspaceRoute(['rounds', 'old-round', 'questions', 'opened'], 'round', 'opened', rows)).toBeNull()
    expect(questionFromWorkspaceRoute(['rounds', 'round', 'questions', 'unopened'], 'round', 'opened', rows)).toBeNull()
    expect(questionFromWorkspaceRoute(['opened'], 'round', null, rows)).toBeNull()
    expect(questionFromWorkspaceRoute(undefined, 'round', 'opened', rows)).toBe('opened')
  })
  test('a pending result, pause, countdown or completed match never enables flag intake', () => {
    expect(canPlayLiveSolo({ state: 'Running' }, { state: 'Running', paused: false })).toBe(true)
    for (const state of ['Countdown', 'Paused', 'Completed', 'Canceled'] as const)
      expect(canPlayLiveSolo({ state }, { state: 'Running' })).toBe(false)
    expect(canPlayLiveSolo({ state: 'Running' }, { state: 'ConfirmingResult' })).toBe(false)
    expect(canPlayLiveSolo({ state: 'Running' }, { state: 'Running', paused: true })).toBe(false)
  })
  test('observers and participants never receive judge controls', () => {
    for (const role of ['Administrator', 'Owner', 'Manager', 'Judge']) expect(canJudgeLiveSolo(role)).toBe(true)
    for (const role of [null, undefined, 'Observer', 'Participant']) expect(canJudgeLiveSolo(role)).toBe(false)
  })
  test('display clock stops during pause or result confirmation and clamps at the server limit', () => {
    const round = { startedAt: '2026-10-09T00:00:00Z', state: 'Running' as const, limitSeconds: 900, activeElapsedMilliseconds: 1000 }
    expect(formatRoundClock(roundRemainingSeconds(round, 2000))).toBe('14:57')
    expect(roundRemainingSeconds({ ...round, paused: true }, 10000)).toBe(899)
    expect(roundRemainingSeconds({ ...round, state: 'ConfirmingResult' }, 10000)).toBe(899)
    expect(roundRemainingSeconds(round, 1000000)).toBe(0)
    expect(formatRoundClock(roundRemainingSeconds(null, 0))).toBe('—')
  })
  test('LiveSolo preparation entry does not change the original mode registration and practice policy', () => {
    const team = { registrationStatus: 'Approved' as const, isBanned: false }
    expect(canEnterCompetition({ mode: 'LiveSolo', status: 'Published' }, team)).toBe(true)
    expect(canEnterCompetition({ mode: 'LiveSolo', status: 'Published' }, { ...team, isBanned: true })).toBe(false)
    for (const mode of ['Ctf', 'Awd', 'Awdp', 'Koh'] as const) {
      expect(canEnterCompetition({ mode, status: 'Published' }, team)).toBe(false)
      expect(canEnterCompetition({ mode, status: 'Running' }, team)).toBe(true)
    }
    expect(canEnterCompetition({ mode: 'Ctf', status: 'Finished', practiceModeEnabled: true }, team)).toBe(true)
    expect(canEnterCompetition({ mode: 'LiveSolo', status: 'Draft' }, team)).toBe(false)
  })
})
