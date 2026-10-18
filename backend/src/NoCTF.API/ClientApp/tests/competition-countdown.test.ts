import { expect, test } from 'bun:test'
import { computed, effectScope, markRaw, ref, watch } from 'vue'
import type { NoCtfapiEndpointsCompetitionsCompetitionResponse as Competition } from '../app/api'
import { canEnterCompetition, canRegisterForCompetition, isCtfPracticeOpen } from '../app/lib/competition-participation'
import { featureController } from './support/feature-controller'

type Controller = typeof import('../app/features/competitions/useCompetitionOverview').useCompetitionOverview
const current = Date.parse('2026-10-17T17:00:00+08:00')
const startTime = '2026-10-24T09:00:00+08:00'
const endTime = '2026-11-07T21:00:00+08:00'

async function fixture(changes: Partial<Competition> = {}, clock = current) {
  const competition: Competition = { id: 'contest', mode: 'Ctf', status: 'Published', startTime, endTime, ...changes }
  const mounted: Array<() => unknown> = [], unmounted: Array<() => void> = []
  let tick: (() => void) | undefined, interval: number | undefined, cleared = false
  class ClockDate extends Date { static override now() { return clock } }
  const dependencies: Record<string, unknown> = {
    computed, ref, watch, markRaw, Date: ClockDate,
    useAuth: () => ({ user: ref(null), isLoggedIn: ref(false), isAdministrator: ref(false) }),
    useCompetitionPoster: () => ({ posterUrl: ref(null), cancelPoster: () => {} }),
    getCompetitionEndpoint: async () => ({ data: competition }),
    listCompetitionTeamsEndpoint: async () => ({ data: { items: [] } }),
    canEnterCompetition, canRegisterForCompetition, isCtfPracticeOpen, LifecycleBadgeComponent: {},
    translate: (key: string) => key, watchCompetition: () => () => {},
    onMounted: (callback: () => unknown) => mounted.push(callback),
    onUnmounted: (callback: () => void) => unmounted.push(callback),
    setInterval: (callback: () => void, milliseconds: number) => { tick = callback; interval = milliseconds; return 1 },
    clearInterval: (id?: number) => { if (id === 1) cleared = true },
  }
  for (const icon of ['ArrowRight', 'Box', 'CalendarRange', 'Clock', 'EyeOff', 'FileText', 'KeyRound', 'LogIn', 'Settings', 'ShieldCheck', 'Trophy', 'UserPlus', 'Users'])
    dependencies[icon] = {}
  const make = await featureController<ReturnType<Controller>, Parameters<Controller>>('features/competitions/useCompetitionOverview.ts', 'useCompetitionOverview', dependencies)
  const scope = effectScope(), state = scope.run(() => make({ competition }, () => {}))!
  return {
    state,
    mount: async () => { for (const callback of mounted) await callback() },
    advance: (time: number) => { clock = time; tick?.() },
    interval: () => interval,
    cleared: () => cleared,
    dispose: () => { unmounted.forEach(callback => callback()); scope.stop() },
  }
}

test('upcoming competition counts down to its start in the screenshot timezone', async () => {
  const f = await fixture()
  try { expect(f.state.countdown.value).toEqual({ label: 'competitions.label.start', ms: 576_000_000 }) }
  finally { f.dispose() }
})

test('a future start does not require an end date to show its countdown', async () => {
  const f = await fixture({ endTime: undefined })
  try { expect(f.state.countdown.value).toEqual({ label: 'competitions.label.start', ms: 576_000_000 }) }
  finally { f.dispose() }
})

test('at the start boundary the overview switches to time until the end', async () => {
  const f = await fixture({}, Date.parse(startTime))
  try { expect(f.state.countdown.value).toEqual({ label: 'competitions.label.end', ms: 1_252_800_000 }) }
  finally { f.dispose() }
})

test('running competitions count down to the end', async () => {
  const f = await fixture({ status: 'Running' }, Date.parse(startTime) + 1_000)
  try { expect(f.state.countdown.value).toEqual({ label: 'competitions.label.end', ms: 1_252_799_000 }) }
  finally { f.dispose() }
})

test('at the end boundary the overview shows finished without negative time', async () => {
  const f = await fixture({}, Date.parse(endTime))
  try { expect(f.state.countdown.value).toEqual({ label: 'common.label.finished', ms: 0 }) }
  finally { f.dispose() }
})

test('an explicitly finished competition overrides future schedule dates', async () => {
  const f = await fixture({ status: 'Finished' })
  try { expect(f.state.countdown.value).toEqual({ label: 'common.label.finished', ms: 0 }) }
  finally { f.dispose() }
})

test('invalid schedule dates do not render an invalid duration', async () => {
  const f = await fixture({ startTime: 'invalid', endTime: 'invalid' })
  try { expect(f.state.countdown.value).toBeNull() }
  finally { f.dispose() }
})

test('the overview refreshes each second, crosses the start boundary and disposes its timer', async () => {
  const f = await fixture({}, Date.parse(startTime) - 1_000)
  try {
    await f.mount()
    expect(f.interval()).toBe(1_000)
    expect(f.state.countdown.value).toEqual({ label: 'competitions.label.start', ms: 1_000 })
    f.advance(Date.parse(startTime))
    expect(f.state.countdown.value).toEqual({ label: 'competitions.label.end', ms: 1_252_800_000 })
  }
  finally { f.dispose() }
  expect(f.cleared()).toBe(true)
})
