import { sourceFile } from './support/feature-source'
import { describe, expect, test } from 'bun:test'
import { createLatestRequestGuard } from '../app/lib/latest-request'

describe('latest request guard', () => {
  test('accepts only the latest request', () => {
    const guard = createLatestRequestGuard()
    const first = guard.begin()
    const second = guard.begin()

    expect(guard.isCurrent(first)).toBeFalse()
    expect(guard.isCurrent(second)).toBeTrue()
  })

  test('invalidates an in-flight request when its view closes', () => {
    const guard = createLatestRequestGuard()
    const request = guard.begin()

    guard.invalidate()

    expect(guard.isCurrent(request)).toBeFalse()
  })

  test('guards every detail loader that can trigger actions', async () => {
    const files = [
      '../app/pages/admin/competitions/[id]/cheats.vue',
      '../app/pages/competitions/[id]/questions.vue',
      '../app/features/notifications/NotificationCenter.vue',
      '../app/pages/admin/platform/users.vue',
    ]

    for (const file of files) {
      const source = await sourceFile(new URL(file, import.meta.url)).text()
      expect(source).toContain('createLatestRequestGuard')
      expect(source).toContain('.isCurrent(request)')
    }
  })
})
