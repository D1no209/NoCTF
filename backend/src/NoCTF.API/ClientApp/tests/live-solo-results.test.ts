import { expect, test } from 'bun:test'
import { emptyResultObservation, observeResult, type ResultObservation } from '../app/features/live-solo/result-observation'
import { resultPresentation } from '../app/features/live-solo/result-presentation'

const active: ResultObservation = { matchId: 'match', matchActive: true, matchEnded: false, matchRevision: 0,
  roundId: 'round', roundActive: true, roundEnded: false, roundRevision: 0, defer: false }
const roundEnd = { ...active, roundActive: false, roundEnded: true, roundRevision: 1 }
const decisive = { ...roundEnd, matchActive: false, matchEnded: true, matchRevision: 1 }

test('historical first entry establishes a stable baseline with no automatic prompt', () => {
  const first = observeResult(emptyResultObservation(), decisive)
  expect(first.prompt).toBeNull(); expect(first.state.matchRevision).toBe(1)
  expect(observeResult(first.state, decisive).prompt).toBeNull()
})
test('active observation survives refresh and only a confirmed result prompts once', () => {
  const saved = structuredClone(observeResult(emptyResultObservation(), active).state)
  const first = observeResult(saved, roundEnd)
  expect(first.prompt).toBe('Round')
  expect(observeResult(first.state, roundEnd).prompt).toBeNull()
  expect(observeResult(first.state, { ...roundEnd, roundRevision: 0 }).prompt).toBeNull()
})
test('decisive round merges into a single series result and consumes both revisions', () => {
  const first = observeResult(observeResult(emptyResultObservation(), active).state, decisive)
  expect(first.prompt).toBe('Match'); expect(first.state.roundRevisions.round).toBe(1)
  expect(observeResult(first.state, decisive).prompt).toBeNull()
})
test('editing defers consumption until the latest authoritative correction can be shown', () => {
  const saved = observeResult(emptyResultObservation(), active).state
  const deferred = observeResult(saved, { ...decisive, defer: true })
  expect(deferred.prompt).toBeNull(); expect(deferred.state.matchRevision).toBe(0)
  const corrected = observeResult(deferred.state, { ...decisive, matchRevision: 2 })
  expect(corrected.prompt).toBe('Match'); expect(corrected.state.matchRevision).toBe(2)
})
test('separate round ids and increasing result revisions are independent of duplicate or late notices', () => {
  let saved = observeResult(emptyResultObservation(), active).state
  saved = observeResult(saved, roundEnd).state
  expect(observeResult(saved, { ...roundEnd, roundRevision: 2 }).prompt).toBe('Round')
  saved = observeResult(saved, { ...active, roundId: 'next' }).state
  expect(observeResult(saved, { ...roundEnd, roundId: 'next' }).prompt).toBe('Round')
})
test('forfeit displays the actual zero tally and timeout does not fabricate a winner', () => {
  const match = { id: 'match', leftTeamId: 'left', leftTeamName: 'FS', rightTeamId: 'right', rightTeamName: 'Blue',
    winnerTeamId: 'right', resultRevision: 1, endReason: 'Forfeit' as const, resultLeftWins: 0, resultRightWins: 0 }
  expect(resultPresentation(match, null, 'Match')).toMatchObject({ hasWinner: true, winnerName: 'Blue', leftName: 'FS', rightName: 'Blue', leftScore: 0, rightScore: 0, score: '0 : 0', reasonKey: 'liveSolo.result.reason.Forfeit' })
  expect(resultPresentation(match, { id: 'round', resultRevision: 1, endReason: 'TimeLimit' }, 'Round')).toMatchObject({ hasWinner: false, winnerName: null, score: null })
})

test('round presentation keeps its winner and frozen scores independent of the current match tally', () => {
  const match = { leftTeamId: 'left', leftTeamName: 'FS', rightTeamId: 'right', rightTeamName: 'Blue',
    winnerTeamId: 'right', leftWins: 2, rightWins: 3 }
  const round = { number: 1, resultRevision: 1, winnerTeamId: 'left', resultLeftWins: 1, resultRightWins: 0, endReason: 'CorrectAnswer' as const }
  expect(resultPresentation(match, round, 'Round')).toMatchObject({ kind: 'Round', roundNumber: 1,
    hasWinner: true, winnerName: 'FS', winnerAvatar: 'left', leftScore: 1, rightScore: 0 })
})

test('a missing winner never matches an unassigned side', () => {
  expect(resultPresentation({ resultRevision: 1, endReason: 'Voided', leftTeamName: 'Unassigned' }, null, 'Match'))
    .toMatchObject({ hasWinner: false, winnerName: null, winnerAvatar: null })
})
