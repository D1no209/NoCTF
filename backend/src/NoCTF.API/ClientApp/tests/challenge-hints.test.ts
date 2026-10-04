import { sourceFile } from './support/feature-source'
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
    const component = await sourceFile(new URL('../app/features/challenges/ChallengeHints.vue', import.meta.url)).text()
    const detail = await sourceFile(new URL('../app/features/challenges/CompetitionChallengeDetail.vue', import.meta.url)).text()
    expect(detail).toContain("<component :is=\"ChallengeHints\"")
    expect(detail).toContain(':hints="challenge.hints"')
    expect(component).toContain('confirmingId.value !== hint.id')
    expect(component).toMatch(/api\.api\.v1\.competitions\.byCompetitionId\([^)]*\)\.challenges\.byCompetitionChallengeId\([^)]*\)\.hints\.byHintId\([^)]*\)\.unlock\.post\(/)
    expect(component).toMatch(/api\.api\.v1\.competitions\.byCompetitionId\([^)]*\)\.gameplayFacts\.byGameplayFactId\([^)]*\)\.get\(/)
    expect(component).toContain('onReconnected:')
    expect(component).toContain('pendingFactId.value = data.gameplayFactId')
    expect(component).not.toMatch(/api\.api\.v1\.admin\.competitions\.byCompetitionId\([^)]*\)\.challenges\.byCompetitionChallengeId\([^)]*\)\.hints\.get\(/)
    expect(component).not.toContain('v-html')
    expect(component).toContain('<MarkdownQuote v-if="readableHintContent(hint) !== null"')
  })
})

describe('native scrollbar theme and main navigation', () => {
  test('keeps the header horizontally accessible without a vertical scrollbar', async () => {
    const layout = await sourceFile(new URL('../app/layouts/default.vue', import.meta.url)).text()
    expect(layout).toContain('overflow-x-auto overflow-y-hidden')
    expect(layout).toContain('scrollbar-none')
    expect(layout).toContain('data-position="center"')
    expect(layout).not.toContain("{ to: '/admin/competitions'")
  })

  test('declares native light and dark schemes instead of globally hiding scrollbars', async () => {
    const css = await sourceFile(new URL('../app/assets/css/main.css', import.meta.url)).text()
    expect(css).toMatch(/:root\s*\{\s*color-scheme: light;/)
    expect(css).toMatch(/\.dark\s*\{\s*color-scheme: dark;/)
    expect(css).toContain('@utility scrollbar-none')
    expect(css).not.toMatch(/\*\s*\{[^}]*scrollbar-width:\s*none/s)
  })
})
