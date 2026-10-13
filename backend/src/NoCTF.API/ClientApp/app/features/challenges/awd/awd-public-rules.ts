import type { NoCtfapiEndpointsChallengesAwdPublicRulesResponse as Rules, NoCtfapiEndpointsChallengesAwdPublicRulePolicyResponse as Policy } from '../../../api'
import { competitionConfigFields, type ConfigValues, type ConfigFieldDef } from '../../../utils/game-config'
import { awdFieldVisible, awdPresetConstraints } from '../../../utils/awd-config-fields'
import { translate } from '../../../utils/i18n'

/** Read resolved server values. Metadata is shared with the structured editor, never used to recompute scores. */
export function publicPolicyValues(policy?: Policy, effective = true): ConfigValues {
  return { ...policy?.options, attackRewardMode: policy?.attackMode, attackPoints: policy?.attackPoints,
    victimDefensePoolPoints: policy?.defensePoints, serviceHealthyPoints: policy?.slaPoints,
    checkerIntervalSeconds: policy?.checkerIntervalSeconds, ...(effective ? policy?.scoring : {}) } as ConfigValues
}
export function publicRuleSource(rules: Rules, field: string, next: boolean, policy?: Policy): string {
  if (!next) return translate(policy?.scoringChangeEventId && field in (policy.scoring ?? {})
    ? 'challenges.awdRules.source.amendment' : 'challenges.awdRules.source.issued')
  if (field in awdPresetConstraints(publicPolicyValues(rules.nextRound))) return translate('challenges.awdRules.source.preset')
  const base: Record<string, boolean | undefined> = { attackPoints: rules.attackPointsOverridden,
    victimDefensePoolPoints: rules.defensePointsOverridden, serviceHealthyPoints: rules.slaPointsOverridden,
    checkerIntervalSeconds: rules.checkerIntervalOverridden, attackRewardMode: rules.attackModeOverridden }
  const overridden = base[field] ?? rules.questionOverrides?.[field as keyof NonNullable<Rules['questionOverrides']>] != null
  return translate(overridden ? 'challenges.awdRules.source.question' : 'challenges.awdRules.source.competition')
}
export function publicRuleGroups(rules: Rules, policy: Policy | undefined, next: boolean) {
  const values = publicPolicyValues(policy), issued = publicPolicyValues(policy, false)
  if (next) Object.assign(values, { roundDurationSeconds: rules.roundDurationSeconds, hardeningDurationSeconds: rules.hardeningDurationSeconds })
  const fields = competitionConfigFields('Awd').filter(field => field.key !== 'flagTemplate'
    && (next || !['roundDurationSeconds', 'hardeningDurationSeconds', 'preset'].includes(field.key))
    && awdFieldVisible(field, values))
  function format(field: ConfigFieldDef, value: unknown) {
    if (value === null && field.key === 'maximumRoundPenalty') return translate('challenges.awdRules.noCap')
    if (value === null && field.key === 'minimumTotalScore') return translate('challenges.awdRules.noFloor')
    if (value === null && field.key === 'totalRounds') return translate('challenges.awdRules.durationBased')
    if (value == null) return '—'
    if (typeof value === 'boolean') return translate(value ? 'challenges.awdRules.yes' : 'challenges.awdRules.no')
    const option = field.options?.find(option => option.value === value)
    return option ? translate(option.label) : String(value)
  }
  return ['timeline', 'flags', 'scoring', 'checker', 'sla', 'penalty', 'composition'].map(group => ({ key: group,
    label: translate(`administration.awd.group.${group}`), rows: fields.filter(field => field.group === group).map(field => ({
      key: field.key, label: field.label, value: format(field, values[field.key]),
      issued: !next && values[field.key] !== issued[field.key] ? format(field, issued[field.key]) : null,
      source: publicRuleSource(rules, field.key, next, policy),
    })) })).filter(group => group.rows.length)
}
export function publicFlagWindow(policy: Policy | undefined, historical: boolean): string {
  const options = policy?.options
  const follow = !historical || options?.linkHistoricalWindow
  const lifetime = follow ? options?.flagLifetime : options?.historicalLifetime
  const unit = follow ? options?.flagLifetimeUnit : options?.historicalLifetimeUnit
  if (lifetime == null || unit == null) return '—'
  return translate(unit === 'Rounds' ? 'challenges.awdRules.roundWindow' : 'challenges.awdRules.secondsWindow', { count: lifetime })
}
