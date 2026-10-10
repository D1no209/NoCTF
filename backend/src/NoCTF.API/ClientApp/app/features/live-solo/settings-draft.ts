import type { NoCtfapiEndpointsLiveSoloLiveSoloConfigurationContract as Configuration } from '../../api'
import type { MessageKey } from '../../locales/en'

export type LiveSoloSettingsDraft = Required<Omit<Configuration, 'stageRules'>> & { stageRules: Required<NonNullable<Configuration['stageRules']>[number]>[] }
export type NumberSetting = 'requiredWins' | 'countdownSeconds' | 'questionIntervalSeconds' | 'roundLimitSeconds' | 'publicDelaySeconds' | 'recordingRetentionDays' | 'maximumConcurrentMatches' | 'maximumRosterMembers' | 'maximumViewers'
export const settingFields: { key: NumberSetting; label: MessageKey; min: number; max: number; section: 'match' | 'timing' | 'media' }[] = [
  { key: 'requiredWins', label: 'liveSolo.settings.wins', min: 1, max: 1024, section: 'match' },
  { key: 'maximumRosterMembers', label: 'liveSolo.settings.roster', min: 1, max: 64, section: 'match' },
  { key: 'maximumConcurrentMatches', label: 'liveSolo.settings.parallel', min: 1, max: 128, section: 'match' },
  { key: 'countdownSeconds', label: 'liveSolo.settings.countdown', min: 1, max: 60, section: 'timing' },
  { key: 'questionIntervalSeconds', label: 'liveSolo.settings.interval', min: 1, max: 86400, section: 'timing' },
  { key: 'roundLimitSeconds', label: 'liveSolo.settings.limit', min: 1, max: 86400, section: 'timing' },
  { key: 'publicDelaySeconds', label: 'liveSolo.settings.delay', min: 0, max: 86400, section: 'media' },
  { key: 'recordingRetentionDays', label: 'liveSolo.settings.retention', min: 1, max: 3650, section: 'media' },
  { key: 'maximumViewers', label: 'liveSolo.settings.viewers', min: 1, max: 100000, section: 'media' },
]
export function settingsDraft(value: Configuration): LiveSoloSettingsDraft {
  return { enabled: value.enabled ?? false, platformStreamingEnabled: value.platformStreamingEnabled ?? false, bracketFormat: value.bracketFormat ?? 'SingleElimination', requiredWins: value.requiredWins ?? 2,
    countdownSeconds: value.countdownSeconds ?? 5, questionIntervalSeconds: value.questionIntervalSeconds ?? 180, roundLimitSeconds: value.roundLimitSeconds ?? 900,
    publicDelaySeconds: value.publicDelaySeconds ?? 60, participantsMayViewOpponents: value.participantsMayViewOpponents ?? false,
    recordingEnabled: value.recordingEnabled ?? false, recordingRetentionDays: value.recordingRetentionDays ?? 30,
    maximumConcurrentMatches: value.maximumConcurrentMatches ?? 4, maximumRosterMembers: value.maximumRosterMembers ?? 2,
    maximumViewers: value.maximumViewers ?? 50,
    stageRules: (value.stageRules ?? []).map(row => ({ lane: row.lane ?? 'Winners', stage: row.stage ?? 1, requiredWins: row.requiredWins ?? 2 })) }
}
export function validSettings(value: LiveSoloSettingsDraft): boolean {
  return settingFields.every(field => Number.isInteger(value[field.key]) && value[field.key] >= field.min && value[field.key] <= field.max)
    && ['SingleElimination', 'DoubleElimination'].includes(value.bracketFormat)
    && value.stageRules.every(row => ['Winners', 'Losers', 'GrandFinal', 'ResetFinal'].includes(row.lane)
      && Number.isInteger(row.stage) && row.stage >= 1 && Number.isInteger(row.requiredWins) && row.requiredWins >= 1 && row.requiredWins <= 1024)
    && new Set(value.stageRules.map(row => `${row.lane}:${row.stage}`)).size === value.stageRules.length
}
export function canManageLiveSolo(role: string | null | undefined) { return ['Administrator', 'Owner', 'Manager'].includes(role ?? '') }
