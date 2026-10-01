import { expect, test } from 'bun:test'
import { buildConfigValues, challengeRuleFields, competitionConfigFields, readConfigValues } from '../app/utils/game-config'

test('CTF global settlement mode defaults to the existing behavior and serializes the selected protocol', () => {
  const fields = competitionConfigFields('Ctf')
  const parsed = readConfigValues({ mode: 'Ctf', flagTemplate: {}, ctf: {} }, fields, { rules: false })!
  expect(parsed.values.scoreSettlementMode).toBe('DynamicRecalculation')
  parsed.values.scoreSettlementMode = 'AtSolve'
  const written = buildConfigValues('Ctf', fields, parsed.values, { rules: false })
  expect(written.ctf).toMatchObject({ scoreSettlementMode: 'AtSolve' })
})

test('CTF challenge settlement override is independent and null restores inheritance', () => {
  const fields = challengeRuleFields('Ctf')
  const inherited = readConfigValues({ mode: 'Ctf', ctf: { scoreSettlementMode: null } }, fields, { rules: true })!
  expect(inherited.overridden.scoreSettlementMode).toBeFalse()
  const parsed = readConfigValues({ mode: 'Ctf', ctf: { scoreSettlementMode: 'AtSolve', scoreCurve: null } }, fields, { rules: true })!
  expect(parsed.overridden.scoreSettlementMode).toBeTrue()
  expect(parsed.overridden.scoreCurve).toBeFalse()
  expect(buildConfigValues('Ctf', fields, parsed.values, { rules: true, overridden: parsed.overridden }).ctf)
    .toMatchObject({ scoreSettlementMode: 'AtSolve' })
  parsed.overridden.scoreSettlementMode = false
  expect(buildConfigValues('Ctf', fields, parsed.values, { rules: true, overridden: parsed.overridden }).ctf)
    .not.toHaveProperty('scoreSettlementMode')
  for (const mode of ['Awd', 'Awdp', 'Koh'] as const) {
    expect(competitionConfigFields(mode).some(field => field.key === 'scoreSettlementMode')).toBeFalse()
    expect(challengeRuleFields(mode).some(field => field.key === 'scoreSettlementMode')).toBeFalse()
  }
})
