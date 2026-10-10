import { expect, test } from 'bun:test'
import { segmentIdFromUrl, stateForPlayingSegment, rememberPublishedStates } from '../app/features/live-solo/program-state'
import { fragmentAtPlaybackTime } from '../app/components/ui/media-preview/media-timeline'

test('program state is bound to the played fragment and never substitutes a newer latest score', () => {
  const rows = [{ id: 'old-video', state: { leftWins: 0 } }, { id: 'new-video', state: { leftWins: 1 } }]
  expect(stateForPlayingSegment(rows, null)).toBeNull()
  expect(stateForPlayingSegment(rows, 'old-video')).toEqual({ leftWins: 0 })
  expect(stateForPlayingSegment(rows, 'expired-video')).toBeNull()
  expect(stateForPlayingSegment(rows, 'new-video')).toEqual({ leftWins: 1 })
})

test('fragment references must be same-origin opaque program segment routes', () => {
  const id = '00000001-0000-4000-8000-000000000001', base = 'https://noctf.invalid'
  expect(segmentIdFromUrl(`/api/v1/competitions/id/live-solo/matches/match/program/segments/${id}`, base)).toBe(id)
  expect(segmentIdFromUrl(`https://external.invalid/program/segments/${id}`, base)).toBeNull()
  expect(segmentIdFromUrl(`/api/v1/runtime-proxies/${id}/0`, base)).toBeNull()
})

test('preloaded fragments cannot advance the state ahead of video playback', () => {
  const ranges = [{ url: 'first', start: 0, end: 2 }, { url: 'second', start: 2, end: 4 }, { url: 'latest', start: 4, end: 6 }]
  expect(fragmentAtPlaybackTime(ranges, 0, false)).toBeNull()
  expect(fragmentAtPlaybackTime(ranges, 1, true)).toBe('first')
  expect(fragmentAtPlaybackTime(ranges, 2, true)).toBe('second')
  expect(fragmentAtPlaybackTime(ranges, 5, true)).toBe('latest')
  expect(fragmentAtPlaybackTime(ranges, 20, true)).toBeNull()
})
test('already authorized buffered video keeps its immutable frame when the server window shrinks', () => {
  const first=rememberPublishedStates(new Map(),[{id:'playing',state:{leftWins:0}},{id:'newer',state:{leftWins:1}}],null)
  const shrunk=rememberPublishedStates(first,[{id:'newer',state:{leftWins:9}},{id:'latest',state:{leftWins:2}}],'playing',2)
  expect(shrunk.get('playing')).toEqual({leftWins:0})
  expect(shrunk.get('latest')).toEqual({leftWins:2})
  expect(shrunk.size).toBe(2)
  expect(shrunk.get('absent')).toBeUndefined()
})
