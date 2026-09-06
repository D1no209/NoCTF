import { describe, expect, test } from 'bun:test'
import { affectsChallengeHints, hintUnlockState, readableHintContent } from '../app/lib/challenge-hints'

describe('participant hints', () => {
  test('does not reveal content unless the server marks it unlocked', () => {
    expect(readableHintContent({ content: 'private', isUnlocked: false })).toBeNull()
    expect(readableHintContent({ content: 'private' })).toBeNull()
    expect(readableHintContent({ content: 'free', cost: 0, isUnlocked: true })).toBe('free')
    expect(readableHintContent({ content: 'paid', cost: 20, isUnlocked: true })).toBe('paid')
  })

  test('waits for async unlock adjudication and rejects failed results', () => {
    for (const state of ['Pending', 'Queued', 'Processing'] as const)
      expect(hintUnlockState({ state })).toBe('pending')
    expect(hintUnlockState({ state: 'Completed', result: 'Unlocked' })).toBe('unlocked')
    expect(hintUnlockState({ state: 'Completed', result: 'Rejected' })).toBe('failed')
    expect(hintUnlockState({ state: 'PlatformFailed' })).toBe('failed')
  })

  test('reloads on hint publication and unlock without refreshing for unrelated events', () => {
    expect(affectsChallengeHints('HintPublished')).toBeTrue()
    expect(affectsChallengeHints('HintUnlocked')).toBeTrue()
    expect(affectsChallengeHints('ChallengeUpdated')).toBeTrue()
    expect(affectsChallengeHints('RuntimeCreated')).toBeFalse()
  })

  test('uses participant APIs, inline cost confirmation, polling and safe text rendering', async () => {
    const component = await Bun.file(new URL('../app/components/challenges/ChallengeHints.vue', import.meta.url)).text()
    const detail = await Bun.file(new URL('../app/components/challenges/CompetitionChallengeDetail.vue', import.meta.url)).text()
    expect(detail).toContain('<ChallengeHints')
    expect(detail).toContain(':hints="challenge.hints"')
    expect(component).toContain('confirmingId.value !== hint.id')
    expect(component).toContain('unlockChallengeHintEndpoint({')
    expect(component).toContain('getGameplayFactStatusEndpoint({')
    expect(component).toContain('onReconnected:')
    expect(component).toContain('pendingFactId.value = data.gameplayFactId')
    expect(component).not.toContain('adminListCompetitionChallengeHints')
    expect(component).not.toContain('v-html')
  })
})

describe('native scrollbar theme and main navigation', () => {
  test('keeps the header horizontally accessible without a vertical scrollbar', async () => {
    const layout = await Bun.file(new URL('../app/layouts/default.vue', import.meta.url)).text()
    expect(layout).toContain('overflow-x-auto overflow-y-hidden')
    expect(layout).toContain('scrollbar-none')
    expect(layout).toContain('py-1')
  })

  test('declares native light and dark schemes instead of globally hiding scrollbars', async () => {
    const css = await Bun.file(new URL('../app/assets/css/main.css', import.meta.url)).text()
    expect(css).toMatch(/:root\s*\{\s*color-scheme: light;/)
    expect(css).toMatch(/\.dark\s*\{\s*color-scheme: dark;/)
    expect(css).toContain('@utility scrollbar-none')
    expect(css).not.toMatch(/\*\s*\{[^}]*scrollbar-width:\s*none/s)
  })
})
