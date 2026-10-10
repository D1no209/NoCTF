import { expect, test } from 'bun:test'
import { allReplaysConfirmed, correctionScoreValid, replayConsents } from '../app/features/live-solo/correction-state'
test('only started downstream matches require explicit consent, including after a refresh changes the set', () => {
  const rows = [{ matchId: 'started', concurrencyStamp: 'one', requiresReplay: true }, { matchId: 'unstarted', concurrencyStamp: 'two', requiresReplay: false }]
  const selected = new Set<string>()
  expect(allReplaysConfirmed(rows, selected)).toBe(false); selected.add('started')
  expect(allReplaysConfirmed(rows, selected)).toBe(true)
  expect(replayConsents(rows, selected)).toEqual([{matchId:'started',concurrencyStamp:'one'}])
  expect(allReplaysConfirmed([...rows, {matchId:'another',concurrencyStamp:'three',requiresReplay:true}],selected)).toBe(false)
})
test('confirmation always carries current server versions and ignores irrelevant checked rows', () => {
  const selected=new Set(['old','new','unstarted'])
  expect(replayConsents([{matchId:'new',concurrencyStamp:'fresh',requiresReplay:true},{matchId:'unstarted',requiresReplay:false}],selected))
    .toEqual([{matchId:'new',concurrencyStamp:'fresh'}])
  expect(allReplaysConfirmed([{matchId:'new',requiresReplay:true}],selected)).toBe(false)
})
test('proposed score requires one exact winning threshold, distinct selected team and integer tallies', () => {
  expect(correctionScoreValid('left','left','right',2,2,1)).toBe(true)
  expect(correctionScoreValid('right','left','right',2,0,2)).toBe(true)
  expect(correctionScoreValid('left','left','right',2,2,2)).toBe(false)
  expect(correctionScoreValid('outsider','left','right',2,2,0)).toBe(false)
  expect(correctionScoreValid('left','left','right',2,2,0.5)).toBe(false)
  expect(correctionScoreValid('left','left','right',2,2,-1)).toBe(false)
})
