import { expect, test } from 'bun:test'
import { canReplaceScoreboardSnapshot } from '../app/utils/scoreboard-coherence'

test('a completed response may advance the displayed snapshot while newer notifications are pending', () => {
  expect(canReplaceScoreboardSnapshot({ version: '11', dataScope: 'Live' }, { version: '10', dataScope: 'Live' })).toBeTrue()
  expect(canReplaceScoreboardSnapshot({ version: '11', dataScope: 'Live' }, { version: '11', dataScope: 'Live' })).toBeTrue()
})

test('a response cannot regress the displayed generation within the same access scope', () => {
  expect(canReplaceScoreboardSnapshot({ version: '9', dataScope: 'Live' }, { version: '10', dataScope: 'Live' })).toBeFalse()
  expect(canReplaceScoreboardSnapshot(null, { version: '10', dataScope: 'Live' })).toBeFalse()
})

test('switching to a captured frozen generation or hidden scope remains valid', () => {
  expect(canReplaceScoreboardSnapshot({ version: '5', dataScope: 'Frozen' }, { version: '10', dataScope: 'Live' })).toBeTrue()
  expect(canReplaceScoreboardSnapshot({ version: '0', dataScope: 'Hidden' }, { version: '10', dataScope: 'Live' })).toBeTrue()
})
