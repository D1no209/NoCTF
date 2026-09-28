import { describe, expect, test } from 'bun:test'
import { readFileSync } from 'node:fs'
import { createRuntimeExtensionRequest, parseRuntimeExtensionMinutes } from '../app/lib/runtime-extension'

describe('runtime extension request', () => {
  const now = Date.parse('2026-09-28T00:00:00Z')
  const expiresAt = new Date(now + 52 * 60_000).toISOString()

  test('adds the requested minutes to the existing expiry even before the final ten minutes', () => {
    expect(createRuntimeExtensionRequest(expiresAt, now, 30, 720)).toEqual({
      minutes: 30,
      expiresAt: new Date(now + 82 * 60_000).toISOString(),
    })
  })

  test('does not coerce empty or invalid minutes into an extension', () => {
    for (const value of ['', ' ', null, undefined, 0, -1, 721, 1.5, Number.NaN]) {
      expect(parseRuntimeExtensionMinutes(value, 720)).toBeNull()
      expect(createRuntimeExtensionRequest(expiresAt, now, value, 720)).toBeNull()
    }
    expect(parseRuntimeExtensionMinutes('30', 720)).toBe(30)
  })

  test('does not extend an expired or malformed runtime', () => {
    expect(createRuntimeExtensionRequest(new Date(now).toISOString(), now, 30, 720)).toBeNull()
    expect(createRuntimeExtensionRequest('invalid', now, 30, 720)).toBeNull()
  })

  test('the participant and template controls keep the complete value visible and disable invalid actions', () => {
    const participant = readFileSync(new URL('../app/components/views/challenges/RuntimeCardView.vue', import.meta.url), 'utf8')
    const template = readFileSync(new URL('../app/components/views/admin/ChallengeTestRuntimePanelView.vue', import.meta.url), 'utf8')
    for (const view of [participant, template]) {
      expect(view).toContain('class="w-36 shrink-0"')
      expect(view).toContain('v-model="extendMinutes"')
      expect(view).toContain('extendMinutesInvalid')
      expect(view).not.toContain('class="w-20"')
    }
    expect(participant).toContain(':disabled="!canExtend"')
    expect(template).toContain(':disabled="!validExtension"')
  })
})
