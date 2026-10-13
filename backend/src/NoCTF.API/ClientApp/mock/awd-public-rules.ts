import type { NoCtfapiEndpointsChallengesAwdPublicRulesResponse as Rules, NoCtfapiEndpointsChallengesAwdPublicRulePolicyResponse as Policy } from '../app/api'
import { resolveAwdPresetValues } from '../app/utils/awd-config-fields'
import { awdScoringPolicy } from '../app/features/admin/awd/awd-scoring-policy'
import { mockModeConfiguration, mockRules } from './data/fixtures'
import type { Data } from './schema'

export function createMockAwdPublicRules() {
  const issued = new Map<string, Policy>()
  return (competition: Data, challenge: Data, template: Data | undefined, settings: Map<string, Data>, ownTeam: Data | undefined,
    amendment?: { scoring: Policy['scoring']; scoringChangeEventId: string | null }): Rules => {
    const config = ((settings.get(`mode-configuration:${competition.id}`) ?? mockModeConfiguration('Awd')) as Data).awd
    const rules = ((settings.get(`challenge-rules:${challenge.id}`) ?? mockRules('Awd')) as Data).awd
    const values = resolveAwdPresetValues({ ...config, ...config.options,
      ...Object.fromEntries(Object.entries(rules.overrides ?? {}).filter(([, value]) => value != null)),
      ...Object.fromEntries(['attackRewardMode', 'attackPoints', 'victimDefensePoolPoints', 'serviceHealthyPoints', 'checkerIntervalSeconds'].map(key => [key, rules[key] ?? config[key]])) })
    const options = Object.fromEntries(Object.keys(config.options).map(key => [key, values[key]]))
    const next: Policy = { attackMode: values.attackRewardMode as Policy['attackMode'], attackPoints: values.attackPoints as number,
      defensePoints: values.victimDefensePoolPoints as number, slaPoints: values.serviceHealthyPoints as number,
      checkerIntervalSeconds: values.checkerIntervalSeconds as number, options, scoring: awdScoringPolicy(values),
      stores: (template?.definition?.awd?.flagStores ?? []).map((store: Data) => {
        const override = rules.storeRules?.find((rule: Data) => rule.storeId === store.id)
        return { storeId: store.id, name: store.name, weight: override?.weight ?? 1, flagsPerIssue: override?.flagsPerIssue ?? values.flagsPerStore }
      }) }
    const now = Date.now(), start = new Date(now - 90000).toISOString()
    if (!issued.has(challenge.id)) issued.set(challenge.id, structuredClone({ ...next, rulesId: `mock-rules-${challenge.id}`, revision: 1, effectiveAt: start }))
    const current = { ...issued.get(challenge.id)!, ...amendment }
    return { serverTime: new Date(now).toISOString(), endsAt: competition.endTime, acceptanceEndsAt: competition.endTime,
      paused: competition.status === 'Paused', acceptingSubmissions: competition.status === 'Running',
      roundNumber: 4, roundId: 'mock-round-4', roundStartsAt: start, roundEndsAt: new Date(now + 90000).toISOString(),
      roundDurationSeconds: config.roundDurationSeconds, hardeningDurationSeconds: config.hardeningDurationSeconds,
      competitionDefaults: config.options, questionOverrides: rules.overrides ?? {}, issuedPolicies: [current], nextRound: next,
      attackPointsOverridden: rules.attackPoints != null, defensePointsOverridden: rules.victimDefensePoolPoints != null,
      slaPointsOverridden: rules.serviceHealthyPoints != null, checkerIntervalOverridden: rules.checkerIntervalSeconds != null,
      attackModeOverridden: rules.attackRewardMode != null,
      ownService: ownTeam ? { enabled: true, health: 'InternalError', execution: 'FailedToDetermine', settlement: 'PendingAdjudication',
        observedAt: new Date(now - 15000).toISOString(), lastTrustedHealth: 'Ok', lastTrustedAt: new Date(now - 195000).toISOString() } : null }
  }
}
