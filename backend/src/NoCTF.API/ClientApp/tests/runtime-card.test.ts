import { describe, expect, test } from 'bun:test'
import {
  normalizePlayerRuntime,
  shouldPollPlayerRuntime,
} from '../app/utils/player-runtime'

type RuntimeSnapshot = Parameters<typeof normalizePlayerRuntime>[0]

const snapshot = (
  state: RuntimeSnapshot extends null ? never : NonNullable<RuntimeSnapshot>['state'],
  expiresAt: string | null = null,
): NonNullable<RuntimeSnapshot> => ({ state, expiresAt })

describe('player runtime presentation', () => {
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
})
