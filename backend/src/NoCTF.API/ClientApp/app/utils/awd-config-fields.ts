import type { NoCtfapiEndpointsAdministrationCompetitionsAwdRuleOptionsContract as AwdOptions, NoCtfapiEndpointsAdministrationChallengesAwdRuleOverridesContract as AwdOverrides } from '../api'
import type { ConfigFieldDef, ConfigValues } from './game-config'
import { translate } from './i18n'

export type AwdFieldGroup = 'timeline' | 'flags' | 'scoring' | 'checker' | 'sla' | 'penalty' | 'composition'

const optionFields = [
  { key: 'flagLifetimeUnit', label: 'administration.awd.field.flagLifetimeUnit', type: 'select', group: 'flags', defaultValue: "Rounds", options: [{ value: 'Rounds', label: 'administration.awd.option.Rounds' }, { value: 'ActiveSeconds', label: 'administration.awd.option.ActiveSeconds' }] },
  { key: 'flagLifetime', label: 'administration.awd.field.flagLifetime', type: 'int', group: 'flags', defaultValue: 5, min: 1 },
  { key: 'linkHistoricalWindow', label: 'administration.awd.field.linkHistoricalWindow', type: 'bool', group: 'flags', defaultValue: true },
  { key: 'historicalLifetimeUnit', label: 'administration.awd.field.historicalLifetimeUnit', type: 'select', group: 'flags', defaultValue: "Rounds", options: [{ value: 'Rounds', label: 'administration.awd.option.Rounds' }, { value: 'ActiveSeconds', label: 'administration.awd.option.ActiveSeconds' }] },
  { key: 'historicalLifetime', label: 'administration.awd.field.historicalLifetime', type: 'int', group: 'flags', defaultValue: 5, min: 1 },
  { key: 'generationPolicy', label: 'administration.awd.field.generationPolicy', type: 'select', group: 'flags', defaultValue: "EveryRound", options: [{ value: 'EveryRound', label: 'administration.awd.option.EveryRound' }, { value: 'EveryNRounds', label: 'administration.awd.option.EveryNRounds' }, { value: 'ActiveInterval', label: 'administration.awd.option.ActiveInterval' }] },
  { key: 'generationInterval', max: 864000, label: 'administration.awd.field.generationInterval', type: 'int', group: 'flags', defaultValue: 1, min: 1 },
  { key: 'flagsPerStore', max: 64, label: 'administration.awd.field.flagsPerStore', type: 'int', group: 'flags', defaultValue: 1, min: 1 },
  { key: 'roundGapSeconds', max: 86400, label: 'administration.awd.field.roundGapSeconds', type: 'int', group: 'timeline', defaultValue: 0, min: 0 },
  { key: 'totalRounds', max: 1000000, label: 'administration.awd.field.totalRounds', type: 'int', group: 'timeline', defaultValue: null, min: 1 },
  { key: 'submissionTailSeconds', max: 86400, label: 'administration.awd.field.submissionTailSeconds', type: 'int', group: 'timeline', defaultValue: 0, min: 0 },
  { key: 'finalCheckBudgetSeconds', max: 300, label: 'administration.awd.field.finalCheckBudgetSeconds', type: 'int', group: 'checker', defaultValue: 30, min: 1 },
  { key: 'retryCount', max: 10, label: 'administration.awd.field.retryCount', type: 'int', group: 'checker', defaultValue: 2, min: 0 },
  { key: 'retryIntervalSeconds', max: 300, label: 'administration.awd.field.retryIntervalSeconds', type: 'int', group: 'checker', defaultValue: 5, min: 1 },
  { key: 'callbackGraceSeconds', max: 300, label: 'administration.awd.field.callbackGraceSeconds', type: 'int', group: 'checker', defaultValue: 30, min: 0 },
  { key: 'slaSettlement', label: 'administration.awd.field.slaSettlement', type: 'select', group: 'checker', defaultValue: "FinalScheduledCheck", options: [{ value: 'FinalScheduledCheck', label: 'administration.awd.option.FinalScheduledCheck' }, { value: 'SampleAverage', label: 'administration.awd.option.SampleAverage' }] },
  { key: 'preset', label: 'administration.awd.field.preset', type: 'select', group: 'scoring', defaultValue: "Fixed", options: [{ value: 'Fixed', label: 'administration.awd.option.Fixed' }, { value: 'SharedPool', label: 'administration.awd.option.SharedPool' }, { value: 'Scarcity', label: 'administration.awd.option.Scarcity' }, { value: 'TargetRank', label: 'administration.awd.option.TargetRank' }, { value: 'SlaMultiplier', label: 'administration.awd.option.SlaMultiplier' }, { value: 'Custom', label: 'administration.awd.option.Custom' }] },
  { key: 'defenseMode', label: 'administration.awd.field.defenseMode', type: 'select', group: 'scoring', defaultValue: "FirstLeak", options: [{ value: 'FirstLeak', label: 'administration.awd.option.FirstLeak' }, { value: 'PerCapture', label: 'administration.awd.option.PerCapture' }, { value: 'SharedPool', label: 'administration.awd.option.SharedPool' }] },
  { key: 'slaBasis', label: 'administration.awd.field.slaBasis', type: 'select', group: 'sla', defaultValue: "StatusRatio", options: [{ value: 'StatusRatio', label: 'administration.awd.option.StatusRatio' }, { value: 'ReadableFlags', label: 'administration.awd.option.ReadableFlags' }] },
  { key: 'okRatio', label: 'administration.awd.field.okRatio', type: 'decimal', group: 'sla', defaultValue: 1, min: 0, max: 1 },
  { key: 'recoveringRatio', label: 'administration.awd.field.recoveringRatio', type: 'decimal', group: 'sla', defaultValue: 0.5, min: 0, max: 1 },
  { key: 'mumbleRatio', label: 'administration.awd.field.mumbleRatio', type: 'decimal', group: 'sla', defaultValue: 0, min: 0, max: 1 },
  { key: 'offlineRatio', label: 'administration.awd.field.offlineRatio', type: 'decimal', group: 'sla', defaultValue: 0, min: 0, max: 1 },
  { key: 'penaltyMode', label: 'administration.awd.field.penaltyMode', type: 'select', group: 'penalty', defaultValue: "SlaRewardOnly", options: [{ value: 'SlaRewardOnly', label: 'administration.awd.option.SlaRewardOnly' }, { value: 'Fixed', label: 'administration.awd.option.Fixed' }, { value: 'Proportional', label: 'administration.awd.option.Proportional' }, { value: 'Combined', label: 'administration.awd.option.Combined' }] },
  { key: 'mumblePenalty', max: 1000000, label: 'administration.awd.field.mumblePenalty', type: 'int', group: 'penalty', defaultValue: 0, min: 0 },
  { key: 'offlinePenalty', max: 1000000, label: 'administration.awd.field.offlinePenalty', type: 'int', group: 'penalty', defaultValue: 0, min: 0 },
  { key: 'recoveringPenalty', max: 1000000, label: 'administration.awd.field.recoveringPenalty', type: 'int', group: 'penalty', defaultValue: 0, min: 0 },
  { key: 'mumblePenaltyRatio', label: 'administration.awd.field.mumblePenaltyRatio', type: 'decimal', group: 'penalty', defaultValue: 0, min: 0, max: 1 },
  { key: 'offlinePenaltyRatio', label: 'administration.awd.field.offlinePenaltyRatio', type: 'decimal', group: 'penalty', defaultValue: 0, min: 0, max: 1 },
  { key: 'recoveringPenaltyRatio', label: 'administration.awd.field.recoveringPenaltyRatio', type: 'decimal', group: 'penalty', defaultValue: 0, min: 0, max: 1 },
  { key: 'consecutiveFailureMultiplier', max: 10, label: 'administration.awd.field.consecutiveFailureMultiplier', type: 'decimal', group: 'penalty', defaultValue: 1, min: 1 },
  { key: 'maximumRoundPenalty', max: 1000000, label: 'administration.awd.field.maximumRoundPenalty', type: 'int', group: 'penalty', defaultValue: null, min: 0 },
  { key: 'minimumTotalScore', min: -1000000, max: 1000000, label: 'administration.awd.field.minimumTotalScore', type: 'int', group: 'composition', defaultValue: null },
  { key: 'confidentialityPoints', max: 1000000, label: 'administration.awd.field.confidentialityPoints', type: 'int', group: 'scoring', defaultValue: 0, min: 0 },
  { key: 'minimumAttackRatio', exclusiveMinimum: true, label: 'administration.awd.field.minimumAttackRatio', type: 'decimal', group: 'scoring', defaultValue: 0.1, min: 0, max: 1 },
  { key: 'scarcityAlpha', exclusiveMinimum: true, max: 10, label: 'administration.awd.field.scarcityAlpha', type: 'decimal', group: 'scoring', defaultValue: 0.705, min: 0 },
  { key: 'maximumRankMultiplier', max: 100, label: 'administration.awd.field.maximumRankMultiplier', type: 'decimal', group: 'scoring', defaultValue: 2, min: 1 },
  { key: 'composition', label: 'administration.awd.field.composition', type: 'select', group: 'composition', defaultValue: "Additive", options: [{ value: 'Additive', label: 'administration.awd.option.Additive' }, { value: 'Weighted', label: 'administration.awd.option.Weighted' }, { value: 'PositiveSlaMultiplier', label: 'administration.awd.option.PositiveSlaMultiplier' }] },
  { key: 'attackWeight', max: 100, label: 'administration.awd.field.attackWeight', type: 'decimal', group: 'composition', defaultValue: 1, min: 0 },
  { key: 'defenseWeight', max: 100, label: 'administration.awd.field.defenseWeight', type: 'decimal', group: 'composition', defaultValue: 1, min: 0 },
  { key: 'slaWeight', max: 100, label: 'administration.awd.field.slaWeight', type: 'decimal', group: 'composition', defaultValue: 1, min: 0 },
  { key: 'attackSettlement', label: 'administration.awd.field.attackSettlement', type: 'select', group: 'scoring', defaultValue: "Immediate", options: [{ value: 'Immediate', label: 'administration.awd.option.Immediate' }, { value: 'RoundEnd', label: 'administration.awd.option.RoundEnd' }, { value: 'FlagExpiry', label: 'administration.awd.option.FlagExpiry' }] },
] satisfies (Omit<ConfigFieldDef, 'key'> & { key: keyof AwdOptions })[]

const competitionOnly = new Set<keyof AwdOptions>(['roundGapSeconds', 'totalRounds', 'submissionTailSeconds', 'minimumTotalScore', 'composition', 'attackWeight', 'defenseWeight', 'slaWeight'])

export function awdOptionFields(rules: boolean): ConfigFieldDef[] {
  return optionFields.filter(field => !rules || !competitionOnly.has(field.key)).map(field => ({
    ...field, scope: 'awdOptions', label: translate(field.label),
    options: 'options' in field ? field.options : undefined,
  }))
}

// Compile-time coverage of every nullable question override.
type MissingOverride = Exclude<keyof AwdOverrides, typeof optionFields[number]['key']>
type MissingOption = Exclude<keyof AwdOptions, typeof optionFields[number]['key']>
const completeFieldCoverage: [MissingOverride, MissingOption] extends [never, never] ? true : never = true
void completeFieldCoverage

export function awdFieldVisible(field: ConfigFieldDef, values: ConfigValues): boolean {
  if (['historicalLifetimeUnit', 'historicalLifetime'].includes(field.key)) return values.linkHistoricalWindow === false
  if (field.key === 'generationInterval') return values.generationPolicy !== 'EveryRound'
  if (['minimumAttackRatio', 'scarcityAlpha'].includes(field.key)) return values.attackRewardMode === 'Scarcity'
  if (field.key === 'maximumRankMultiplier') return values.attackRewardMode === 'TargetRank'
  if (['attackWeight', 'defenseWeight', 'slaWeight'].includes(field.key)) return values.composition === 'Weighted'
  if (['okRatio', 'recoveringRatio', 'mumbleRatio', 'offlineRatio'].includes(field.key)) return values.slaBasis === 'StatusRatio'
  if (['mumblePenalty', 'offlinePenalty', 'recoveringPenalty'].includes(field.key)) return ['Fixed', 'Combined'].includes(String(values.penaltyMode))
  if (['mumblePenaltyRatio', 'offlinePenaltyRatio', 'recoveringPenaltyRatio'].includes(field.key)) return ['Proportional', 'Combined'].includes(String(values.penaltyMode))
  if (['consecutiveFailureMultiplier', 'maximumRoundPenalty'].includes(field.key)) return values.penaltyMode !== 'SlaRewardOnly'
  return true
}

export function awdPresetValues(preset: string): ConfigValues {
  if (preset === 'Custom') return { preset }
  const values: ConfigValues = {
    ...Object.fromEntries(optionFields.filter(field => ['scoring', 'sla', 'penalty', 'composition'].includes(field.group))
      .map(field => [field.key, field.defaultValue])),
    preset, attackRewardMode: 'FixedPerAttack', attackPoints: 50,
    victimDefensePoolPoints: 100, serviceHealthyPoints: 100,
  }
  if (preset === 'SharedPool') Object.assign(values, { attackRewardMode: 'SplitVictimDefensePool', defenseMode: 'SharedPool', attackSettlement: 'FlagExpiry' })
  if (preset === 'Scarcity') Object.assign(values, { attackRewardMode: 'Scarcity', attackSettlement: 'FlagExpiry' })
  if (preset === 'TargetRank') values.attackRewardMode = 'TargetRank'
  if (preset === 'SlaMultiplier') values.composition = 'PositiveSlaMultiplier'
  return values
}

/** Match the server's preset constraints while preserving independently configured budgets. */
export function awdPresetConstraints(values: ConfigValues): ConfigValues {
  switch (values.preset) {
    case 'Fixed': return { attackRewardMode: 'FixedPerAttack', defenseMode: 'FirstLeak', composition: 'Additive' }
    case 'SharedPool': return { attackRewardMode: 'SplitVictimDefensePool', defenseMode: 'SharedPool', composition: 'Additive', attackSettlement: 'FlagExpiry' }
    case 'Scarcity': return { attackRewardMode: 'Scarcity', defenseMode: 'FirstLeak', composition: 'Additive', attackSettlement: 'FlagExpiry' }
    case 'TargetRank': return { attackRewardMode: 'TargetRank', defenseMode: 'FirstLeak', composition: 'Additive' }
    case 'SlaMultiplier': return { attackRewardMode: 'FixedPerAttack', defenseMode: 'FirstLeak', composition: 'PositiveSlaMultiplier' }
    default: return {}
  }
}

export function resolveAwdPresetValues(values: ConfigValues): ConfigValues {
  return { ...values, ...awdPresetConstraints(values) }
}

export function awdInvalidFields(fields: ConfigFieldDef[], values: ConfigValues): string[] {
  const invalid = fields.filter(field => {
    const value = values[field.key]
    if (value == null) return field.type !== 'flagTemplate' && !['totalRounds', 'maximumRoundPenalty', 'minimumTotalScore'].includes(field.key)
    if (field.type === 'select') return !field.options?.some(option => option.value === value)
    if (field.type !== 'int' && field.type !== 'decimal') return false
    if (typeof value !== 'number' || !Number.isFinite(value)) return true
    if (field.type === 'int' && !Number.isSafeInteger(value)) return true
    if (field.type === 'decimal' && Math.abs(value * 1e6 - Math.round(value * 1e6)) > 0.000001) return true
    return (field.min !== undefined && (field.exclusiveMinimum ? value <= field.min : value < field.min))
      || (field.max !== undefined && value > field.max)
  }).map(field => field.key)
  for (const [key, unit] of [['flagLifetime', 'flagLifetimeUnit'], ['historicalLifetime', 'historicalLifetimeUnit']]) {
    if (Number(values[key!]) > (values[unit!] === 'Rounds' ? 1000 : 864000)) invalid.push(key!)
  }
  if (['SplitVictimDefensePool', 'Scarcity'].includes(String(values.attackRewardMode)) && values.attackSettlement !== 'FlagExpiry') invalid.push('attackSettlement')
  if ((values.attackRewardMode === 'SplitVictimDefensePool') !== (values.defenseMode === 'SharedPool')) invalid.push('attackRewardMode', 'defenseMode')
  if (values.generationPolicy === 'ActiveInterval' && Number(values.generationInterval) < 5) invalid.push('generationInterval')
  return [...new Set(invalid)]
}
