import { describe, expect, test } from 'bun:test'

describe('platform runtime administration', () => {
  test('adds an administrator-only global container inventory to the platform workspace', async () => {
    const shell = await Bun.file(
      new URL('../app/pages/admin/platform.vue', import.meta.url),
    ).text()
    const page = await Bun.file(
      new URL('../app/pages/admin/platform/runtimes.vue', import.meta.url),
    ).text()

    expect(shell).toContain("'/admin/platform/runtimes'")
    expect(page).toContain("definePageMeta({ middleware: 'platform-admin' })")
    expect(page).toContain('adminPlatformListActiveRuntimes')
    expect(page).toContain('adminPlatformTerminateRuntime')
    expect(page).toContain('adminPlatformForceTerminateRuntime')
    expect(page).toContain("item.scope === 'ChallengeTest'")
    expect(page).toContain('v-else-if="item.runtime?.challengeId"')
    expect(page).toContain('`/admin/challenges/${item.runtime.challengeId}`')
    expect(page).toContain('useCursorPagination<PlatformRuntime>')
    expect(page).toContain('setInterval(() => void refresh(), 10_000)')
    expect(page).not.toContain("fetch('/api")
  })

  test('keeps destructive runtime targets until the generated SDK request completes', async () => {
    const page = await Bun.file(
      new URL('../app/pages/admin/platform/runtimes.vue', import.meta.url),
    ).text()

    expect(page).toContain(':open="terminateTarget !== null"')
    expect(page).toContain('if (!open && !terminatePending) terminateTarget = null')
    expect(page).toContain(':open="forceTerminateTarget !== null"')
    expect(page).toContain('if (!open && !forceTerminatePending) forceTerminateTarget = null')
    expect(page).toContain('terminateTarget.value = null')
    expect(page).toContain('forceTerminateTarget.value = null')
  })
})
