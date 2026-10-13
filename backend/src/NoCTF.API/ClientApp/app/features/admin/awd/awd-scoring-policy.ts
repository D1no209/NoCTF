import type { NoCtfapiEndpointsAdministrationCompetitionsAwdScoringPolicyContract as Policy } from '../../../api'
import { competitionConfigFields, fieldDefaultValue, type ConfigValues } from '../../../utils/game-config'
import { translate } from '../../../utils/i18n'

export const awdScoringKeys = [
  'attackRewardMode', 'attackPoints', 'victimDefensePoolPoints', 'serviceHealthyPoints', 'slaSettlement', 'defenseMode', 'slaBasis',
  'okRatio', 'recoveringRatio', 'mumbleRatio', 'offlineRatio', 'penaltyMode', 'mumblePenalty', 'offlinePenalty', 'recoveringPenalty',
  'mumblePenaltyRatio', 'offlinePenaltyRatio', 'recoveringPenaltyRatio', 'consecutiveFailureMultiplier', 'maximumRoundPenalty',
  'confidentialityPoints', 'minimumAttackRatio', 'scarcityAlpha', 'maximumRankMultiplier', 'composition', 'attackWeight', 'defenseWeight', 'slaWeight', 'attackSettlement',
] as const satisfies readonly (keyof Policy)[]
const allPolicyFields: Exclude<keyof Policy, typeof awdScoringKeys[number]> extends never ? true : never = true
void allPolicyFields

export function awdScoringFields() { return competitionConfigFields('Awd').filter(field => awdScoringKeys.some(key => key === field.key)) }
/** Emit only the explicit scoring contract; issuance and checker execution policy stay out of the payload. */
export function awdScoringPolicy(values: ConfigValues): Policy {
  return Object.fromEntries(awdScoringKeys.map(key => [key, values[key]])) as Policy
}
export function awdScoringValues(policy: Policy): ConfigValues {
  return Object.fromEntries(awdScoringFields().map(field => [field.key, policy[field.key as keyof Policy] ?? fieldDefaultValue(field)]))
}
export function awdPolicyRows(issued?: Policy, effective?: Policy) {
  const fields = awdScoringFields()
  const format = (key: string, value: unknown) => {
    if (value == null) return '—'
    const option = fields.find(field => field.key === key)?.options?.find(option => option.value === value)
    return option ? translate(option.label) : String(value)
  }
  return fields.map(field => ({ key: field.key, label: field.label,
    issued: format(field.key, issued?.[field.key as keyof Policy]), effective: format(field.key, effective?.[field.key as keyof Policy]),
    changed: issued?.[field.key as keyof Policy] !== effective?.[field.key as keyof Policy] }))
}
