import { describe, expect, test } from 'bun:test'
import { createHumanVerificationCoordinator } from '../app/lib/human-verification-coordinator'
import { sourceFile } from './support/feature-source'

describe('human verification coordination', () => {
  test('serializes challenges and settles each grant once', async () => {
    const coordinator = createHumanVerificationCoordinator()
    const first = coordinator.begin()

    expect(first).not.toBeNull()
    expect(coordinator.active).toBeTrue()
    expect(coordinator.begin()).toBeNull()
    expect(coordinator.settle({ 'X-NoCTF-Human-Verification': 'proof-a' })).toBeTrue()
    expect(coordinator.settle({ 'X-NoCTF-Human-Verification': 'proof-b' })).toBeFalse()
    expect(await first).toEqual({ 'X-NoCTF-Human-Verification': 'proof-a' })
    expect(coordinator.active).toBeFalse()

    const cancelled = coordinator.begin()
    expect(coordinator.settle(null)).toBeTrue()
    expect(await cancelled).toBeNull()
  })

  test('wires only participant-sensitive SDK calls through the gate', async () => {
    const sources = await Promise.all([
      sourceFile(new URL('../app/features/routes/auth/useAuthLoginPage.ts', import.meta.url)).text(),
      sourceFile(new URL('../app/features/routes/auth/useAuthRegisterPage.ts', import.meta.url)).text(),
      sourceFile(new URL('../app/features/challenges/useRuntimeCard.ts', import.meta.url)).text(),
      sourceFile(new URL('../app/features/challenges/useFlagSubmit.ts', import.meta.url)).text(),
      sourceFile(new URL('../app/features/challenges/useFixSubmit.ts', import.meta.url)).text(),
    ])

    expect(sources[0]).toContain("requestHumanVerification('login')")
    expect(sources[1]).toContain("requestHumanVerification('registration')")
    expect(sources[2]).toContain("requestHumanVerification('runtime')")
    expect(sources[3]).toContain("requestHumanVerification('evaluation')")
    expect(sources[4]?.match(/requestHumanVerification\('evaluation'\)/g)?.length).toBe(2)
    for (const source of sources)
      expect(source).toContain('verificationHeaders')
  })

  test('uses the supported provider libraries without storing provider secrets', async () => {
    const feature = await Bun.file(new URL('../app/features/security/useHumanVerification.ts', import.meta.url)).text()
    const view = await Bun.file(new URL('../app/components/views/security/HumanVerificationGateView.vue', import.meta.url)).text()
    const platform = await Bun.file(new URL('../app/composables/usePlatform.ts', import.meta.url)).text()

    expect(feature).toContain("import('@cap.js/widget')")
    expect(feature).toContain("'../assets/cap_wasm_bg.wasm'")
    expect(feature).toContain("from '@nuxtjs/turnstile/runtime/components/NuxtTurnstile.vue'")
    expect(view).toContain(':is="TurnstileWidget"')
    expect(feature).toContain("appearance: 'interaction-only'")
    expect(feature).toContain("'X-NoCTF-Human-Verification'")
    expect(feature).toContain("action === 'runtime' && provider.runtimeRequired === false")
    expect(feature).toContain('watch(() => route.fullPath')
    expect(platform).not.toContain('secret')
  })
})
