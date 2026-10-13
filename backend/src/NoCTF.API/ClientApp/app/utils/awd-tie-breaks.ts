import type { NoCtfapiEndpointsAdministrationCompetitionsAwdTieBreakContract } from '../api'
export type AwdTieBreak = Required<NoCtfapiEndpointsAdministrationCompetitionsAwdTieBreakContract>
export const awdTieBreakFields = ['AttackPoints', 'HealthyRounds', 'AttackCount', 'LastAttackAt', 'RegistrationTime'] as const satisfies readonly AwdTieBreak['field'][]
export function defaultAwdTieBreaks(): AwdTieBreak[] {
  return awdTieBreakFields.map(field => ({ field, direction: field === 'LastAttackAt' || field === 'RegistrationTime' ? 'Ascending' : 'Descending' }))
}
export function validAwdTieBreaks(rules: readonly AwdTieBreak[]): boolean {
  return rules.length > 0 && rules.length <= awdTieBreakFields.length
    && new Set(rules.map(rule => rule.field)).size === rules.length
    && rules.every(rule => awdTieBreakFields.includes(rule.field) && ['Ascending', 'Descending'].includes(rule.direction))
}
