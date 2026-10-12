import { describe, expect, test } from 'bun:test'
import { SpectatorChangeWindow } from '../app/features/spectator/spectator-sync'
import { controlIntervals } from '../app/features/spectator/spectator-timeline'
import { localeDomainsForPath } from '../app/locales/route-domains'

const cutoff = '2026-10-12T00:10:00Z'
const event = (id: number) => ({ id: String(id), revision: '1', at: new Date(id * 1000).toISOString() })
describe('public spectator change window', () => {
  test('first snapshot is a baseline; a late confirmation with an old occurrence time still plays once', () => {
    const window = new SpectatorChangeWindow()
    expect(window.accept([event(3)], cutoff, true)).toEqual([])
    expect(window.accept([event(3), event(1)], cutoff, true)).toEqual([event(1)])
    expect(window.accept([event(3), { ...event(1), revision: '2' }], cutoff, true)).toEqual([])
  })
  test('fills more than one page on reconnect and stays bounded without replaying retained facts', () => {
    const window = new SpectatorChangeWindow()
    const batch = (top: number) => Array.from({ length: 2000 }, (_, i) => event(top - i))
    window.accept(batch(2000), cutoff, true)
    expect(window.accept(batch(2600), cutoff, true)).toHaveLength(600)
    expect(window.accept(batch(2800), cutoff, true)).toHaveLength(200)
    expect(window.accept(batch(2800), cutoff, true)).toHaveLength(0)
  })
  test('freeze, thaw and hidden resets never replay old attacks', () => {
    const window = new SpectatorChangeWindow()
    window.accept([event(1)], cutoff, true)
    expect(window.accept([event(2)], cutoff, false)).toEqual([])
    expect(window.accept([event(3), event(2)], cutoff, true)).toEqual([])
    window.reset()
    expect(window.accept([event(4)], cutoff, true)).toEqual([])
  })
  test('loads the spectator catalog only on its two routes', () => {
    for (const mode of ['awd', 'koh']) expect(localeDomainsForPath(`/competitions/id/${mode}-live`)).toContain('spectator')
    expect(localeDomainsForPath('/competitions/id/live')).not.toContain('spectator')
    expect(localeDomainsForPath('/')).not.toContain('spectator')
  })
})
describe('control confirmation timeline', () => {
  test('keeps effective ownership through failed detection and distinguishes uncontrolled', () => {
    const at = (seconds: number) => new Date(seconds * 1000).toISOString()
    const intervals = controlIntervals([
      { id: '1', at: at(2), controllerId: 'a', kind: 'Control' },
      { id: '2', at: at(4), controllerId: 'a', kind: 'DetectionFailed' },
      { id: '3', at: at(6), controllerId: 'a', kind: 'Control' },
      { id: '4', at: at(8), controllerId: null, kind: 'Uncontrolled' },
      { id: '5', at: at(12), controllerId: 'b', kind: 'Control' },
    ], 0, 10000)
    expect(intervals.map(x => [x.start, x.end, x.controllerId, x.unknown])).toEqual([
      [0, 2000, null, true], [2000, 4000, 'a', false], [4000, 6000, 'a', true],
      [6000, 8000, 'a', false], [8000, 10000, null, false],
    ])
  })
  test('does not create score or certainty before a first sample', () => {
    expect(controlIntervals([], 0, 1000)).toEqual([{ start: 0, end: 1000, controllerId: null, unknown: true }])
    expect(controlIntervals([], 0, 0)).toEqual([])
  })
})
