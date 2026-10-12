import { describe, expect, test } from 'bun:test'
import { reactive } from 'vue'
import { awdFieldVisible, awdInvalidFields, awdPresetValues, awdPresetConstraints, resolveAwdPresetValues } from '../app/utils/awd-config-fields'
import {
  buildConfigValues, challengeRuleFields, competitionConfigFields, definitionContractToModel,
  definitionModelToContract, emptyDefinition, fieldDefaultValue, readConfigValues,
  renameRuntimeService, runtimeServiceIsReferenced, emptyRuntimeTemplate,
} from '../app/utils/game-config'

const fields = competitionConfigFields('Awd')
const defaults = () => Object.fromEntries(fields.map(field => [field.key, fieldDefaultValue(field)]))

describe('AWD rule editor', () => {
  test('safe defaults and all nested options survive a read and save', () => {
    const values = defaults()
    expect(values).toMatchObject({ roundDurationSeconds: 180, attackPoints: 50, victimDefensePoolPoints: 100,
      serviceHealthyPoints: 100, flagLifetime: 5, retryCount: 2, penaltyMode: 'SlaRewardOnly',
      flagTemplate: { bodyTemplate: '[SECURE]' } })
    expect(awdInvalidFields(fields, values)).toEqual([])
    const contract = buildConfigValues('Awd', fields, values, { rules: false })
    expect(contract.awd).toMatchObject({ options: { flagLifetime: 5, retryCount: 2, recoveringRatio: 0.5 } })
    const loaded = readConfigValues(contract, fields, { rules: false })!
    expect(loaded.values).toEqual(values)
    expect(buildConfigValues('Awd', fields, loaded.values, { rules: false })).toEqual(contract)
    expect(fields.some(field => field.key === 'serviceUnhealthyPenalty')).toBeFalse()
  })

  test('zero and false overrides stay explicit while inherited and hidden values survive', () => {
    const rulesFields = challengeRuleFields('Awd')
    const storeRules = [{ storeId: 'f7ea012e-f2d1-46b7-9a65-70fce253ea99', weight: 2.5, flagsPerIssue: 3 }]
    const parsed = readConfigValues({ mode: 'Awd', awd: { attackPoints: 0, storeRules,
      overrides: { recoveringRatio: 0, retryCount: 0, linkHistoricalWindow: false, offlineRatio: null,
        historicalLifetime: 10, maximumRoundPenalty: 0 } } }, rulesFields, { rules: true })!
    expect(parsed.overridden).toMatchObject({ recoveringRatio: true, retryCount: true, linkHistoricalWindow: true, offlineRatio: false })
    const saved = buildConfigValues('Awd', rulesFields, reactive(parsed.values), { rules: true, overridden: parsed.overridden })
    expect(saved.awd).toEqual({ attackPoints: 0, storeRules, overrides: {
      recoveringRatio: 0, retryCount: 0, linkHistoricalWindow: false, historicalLifetime: 10, maximumRoundPenalty: 0,
    } })
    expect(rulesFields.some(field => field.key === 'submissionTailSeconds')).toBeFalse()
    expect(rulesFields.some(field => field.key === 'composition')).toBeFalse()
  })

  test.each(['Fixed', 'SharedPool', 'Scarcity', 'TargetRank', 'SlaMultiplier'])('%s preset creates a valid complete scoring combination', preset => {
    const values = { ...defaults(), ...awdPresetValues(preset) }
    expect(awdInvalidFields(fields, values)).toEqual([])
    if (preset === 'SharedPool' || preset === 'Scarcity') expect(values.attackSettlement).toBe('FlagExpiry')
    if (preset === 'SharedPool') expect(values.defenseMode).toBe('SharedPool')
    if (preset === 'SlaMultiplier') expect(values.composition).toBe('PositiveSlaMultiplier')
  })

  test('invalid combinations and excessive precision are rejected without discarding inactive fields', () => {
    const values = { ...defaults(), attackRewardMode: 'SplitVictimDefensePool', recoveringRatio: 0.1234567,
      flagLifetime: 1001, retryCount: 11 }
    expect(awdInvalidFields(fields, values)).toEqual(expect.arrayContaining(['attackSettlement', 'attackRewardMode', 'defenseMode', 'recoveringRatio', 'flagLifetime', 'retryCount']))
    const custom = { ...defaults(), ...awdPresetValues('Custom'), scarcityAlpha: 1.5 }
    const hidden = fields.find(field => field.key === 'scarcityAlpha')!
    expect(awdFieldVisible(hidden, custom)).toBeFalse()
    expect((buildConfigValues('Awd', fields, custom, { rules: false }).awd as { options: { scarcityAlpha: number } }).options.scarcityAlpha).toBe(1.5)
  })

  test('server preset constraints resolve inherited raw values without changing independent budgets', () => {
    const raw = { ...defaults(), preset: 'SharedPool', attackPoints: 70, recoveringRatio: 0.75 }
    const effective = resolveAwdPresetValues(raw)
    expect(effective).toMatchObject({ attackRewardMode: 'SplitVictimDefensePool', defenseMode: 'SharedPool', attackSettlement: 'FlagExpiry',
      attackPoints: 70, recoveringRatio: 0.75 })
    expect(raw.attackRewardMode).toBe('FixedPerAttack')
    const custom = { ...raw, ...awdPresetConstraints(effective), preset: 'Custom', attackPoints: 0 }
    expect(resolveAwdPresetValues(custom)).toMatchObject({ attackPoints: 0, attackRewardMode: 'SplitVictimDefensePool', defenseMode: 'SharedPool' })
    expect(awdInvalidFields(fields, custom)).toEqual([])
  })

  test('blank AWD Flag bodies use SECURE while AWDP keeps its existing default', () => {
    for (const mode of ['Awd', 'Awdp'] as const) {
      const config = buildConfigValues(mode, competitionConfigFields(mode), {
        flagTemplate: { header: 'flag', bodyTemplate: '', leetLiteralText: false },
      }, { rules: false })
      expect(config.flagTemplate).toMatchObject({ bodyTemplate: mode === 'Awd' ? '[SECURE]' : '[GUID]' })
    }
  })

  test('editing a reactive AWD definition retains stores and updates service references', () => {
    const model = reactive(emptyDefinition('Awd'))
    model.runtime = emptyRuntimeTemplate('Awd')
    model.awdFlagStores = [{ id: '981f28b6-8954-41e5-9c70-857e72631791', name: 'database',
      deliveryMode: 'BusinessChecker', targetServiceName: 'main', injectionTimeoutSeconds: 30,
      preservesHistoricalFlags: true }]
    expect(runtimeServiceIsReferenced(model, 'main')).toBeTrue()
    renameRuntimeService(model, 'main', 'web')
    const contract = definitionModelToContract('Awd', model)
    expect(contract.awd?.flagStores?.[0]?.targetServiceName).toBe('web')
    expect(definitionContractToModel(reactive(contract), 'Awd').awdFlagStores).toEqual(model.awdFlagStores)
  })
})
