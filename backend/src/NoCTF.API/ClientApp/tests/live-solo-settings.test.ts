import { describe, expect, test } from 'bun:test'
import { settingsDraft, validSettings, canManageLiveSolo } from '../app/features/live-solo/settings-draft'
describe('LiveSolo typed settings', () => {
  test('editing a visible field preserves viewer policy and independent stage objects', () => {
    const source = { maximumViewers: 73, stageRules: [{ lane: 'Winners' as const, stage: 1, requiredWins: 3 }] }
    const draft = settingsDraft(source); draft.requiredWins = 4; draft.stageRules[0]!.requiredWins = 5
    expect(draft.maximumViewers).toBe(73); expect(source.stageRules[0]!.requiredWins).toBe(3)
    expect(validSettings(draft)).toBe(true)
  })
  test('zero delay is valid; fractional timing and duplicate stages are invalid', () => {
    const draft = settingsDraft({ publicDelaySeconds: 0 }); expect(validSettings(draft)).toBe(true)
    draft.countdownSeconds = 1.5; expect(validSettings(draft)).toBe(false); draft.countdownSeconds = 5
    draft.stageRules = [{ lane: 'Winners', stage: 1, requiredWins: 2 }, { lane: 'Winners', stage: 1, requiredWins: 3 }]
    expect(validSettings(draft)).toBe(false)
  })
  test('judges and observers remain read-only', () => {
    expect(canManageLiveSolo('Administrator')).toBe(true); expect(canManageLiveSolo('Owner')).toBe(true); expect(canManageLiveSolo('Manager')).toBe(true)
    expect(canManageLiveSolo('Judge')).toBe(false); expect(canManageLiveSolo('Observer')).toBe(false); expect(canManageLiveSolo(null)).toBe(false)
  })
})
