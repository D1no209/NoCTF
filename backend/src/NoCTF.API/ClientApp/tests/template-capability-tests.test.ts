import { describe, expect, test } from 'bun:test'
import { selectTemplateTestOption, templateCapabilityTestFinished } from '../app/features/admin/template-capability-tests'

describe('template capability test interactions', () => {
  test('polling continues through final resource cleanup', () => {
    for (const state of ['Queued', 'Applying', 'Checking', 'Finalizing'] as const)
      expect(templateCapabilityTestFinished(state)).toBe(false)
    for (const state of ['Completed', 'PlatformFailed', 'Canceled'] as const)
      expect(templateCapabilityTestFinished(state)).toBe(true)
  })
  test('single-choice and multiple-choice selections use distinct option identities', () => {
    expect(selectTemplateTestOption(['first'], 'second', false, true)).toEqual(['second'])
    expect(selectTemplateTestOption(['first'], 'second', true, true)).toEqual(['first', 'second'])
    expect(selectTemplateTestOption(['first', 'second'], 'second', true, true)).toEqual(['first', 'second'])
    expect(selectTemplateTestOption(['first', 'second'], 'second', true, false)).toEqual(['first'])
  })
})
