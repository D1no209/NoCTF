import { describe, expect, test } from 'bun:test'
import {
  classifyPlayerRuntimeLookup,
  normalizePlayerRuntime,
  shouldPollPlayerRuntime,
} from '../app/utils/player-runtime'

type RuntimeSnapshot = Parameters<typeof normalizePlayerRuntime>[0]

const snapshot = (
  state: RuntimeSnapshot extends null ? never : NonNullable<RuntimeSnapshot>['state'],
  expiresAt: string | null = null,
): NonNullable<RuntimeSnapshot> => ({ state, expiresAt })

describe('player runtime presentation', () => {
  test('only treats an explicit not-found response as an absent runtime', () => {
    expect(classifyPlayerRuntimeLookup(404, true, false)).toBe('missing')
    expect(classifyPlayerRuntimeLookup(401, true, false)).toBe('failed')
    expect(classifyPlayerRuntimeLookup(403, true, false)).toBe('failed')
    expect(classifyPlayerRuntimeLookup(500, true, false)).toBe('failed')
    expect(classifyPlayerRuntimeLookup(undefined, true, false)).toBe('failed')
    expect(classifyPlayerRuntimeLookup(200, false, false)).toBe('failed')
    expect(classifyPlayerRuntimeLookup(200, false, true)).toBe('available')
  })

  test('treats a stopped historical instance as an environment that has not started', () => {
    expect(normalizePlayerRuntime(snapshot('Stopped'))).toBeNull()

    const failed = snapshot('Failed')
    expect(normalizePlayerRuntime(failed)).toBe(failed)
  })

  test('keeps polling an expired running instance until cleanup reaches a terminal state', () => {
    const now = Date.parse('2026-08-09T12:00:00Z')

    expect(shouldPollPlayerRuntime(snapshot('Running', '2026-08-09T11:59:59Z'), now)).toBeTrue()
    expect(shouldPollPlayerRuntime(snapshot('Stopping'), now)).toBeTrue()
    expect(shouldPollPlayerRuntime(snapshot('Stopped'), now)).toBeFalse()
    expect(shouldPollPlayerRuntime(snapshot('Running', '2026-08-09T12:01:00Z'), now)).toBeFalse()
  })

  test('shows load failures without rendering runtime actions', async () => {
    const source = await Bun.file(
      new URL('../app/components/challenges/RuntimeCard.vue', import.meta.url),
    ).text()

    expect(source).toContain('classifyPlayerRuntimeLookup(response?.status')
    expect(source).toContain('<Alert v-if="loadError" variant="destructive">')
    expect(source).toContain('<div v-if="!loadError" class="flex flex-wrap items-center gap-2">')
  })
})
