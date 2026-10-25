import type { NoCtfapiEndpointsLiveSoloLiveSoloConfigurationContract as Configuration } from '../../api'
import type { MessageKey } from '../../locales/en'

export type TournamentStageDraft = Required<NonNullable<Configuration['tournamentStages']>[number]>
export type LiveSoloSettingsDraft = Required<Omit<Configuration, 'stageRules' | 'tournamentStages'>> & {
  stageRules: (Required<Omit<NonNullable<Configuration['stageRules']>[number], 'tournamentStage'>> & { tournamentStage?: number })[]
  tournamentStages: TournamentStageDraft[]
}
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
  return { staticFlagEvidenceRequired: value.staticFlagEvidenceRequired ?? false, enabled: value.enabled ?? false, platformStreamingEnabled: value.platformStreamingEnabled ?? false, bracketFormat: value.bracketFormat ?? 'SingleElimination', requiredWins: value.requiredWins ?? 2,
    countdownSeconds: value.countdownSeconds ?? 5, questionIntervalSeconds: value.questionIntervalSeconds ?? 180, roundLimitSeconds: value.roundLimitSeconds ?? 900,
    publicDelaySeconds: value.publicDelaySeconds ?? 60, participantsMayViewOpponents: value.participantsMayViewOpponents ?? false,
    recordingEnabled: value.recordingEnabled ?? false, recordingRetentionDays: value.recordingRetentionDays ?? 30,
    maximumConcurrentMatches: value.maximumConcurrentMatches ?? 4, maximumRosterMembers: value.maximumRosterMembers ?? 2,
    maximumViewers: value.maximumViewers ?? 50,
    stageRules: (value.stageRules ?? []).map(row => ({ lane: row.lane ?? 'Winners', stage: row.stage ?? 1, requiredWins: row.requiredWins ?? 2, tournamentStage: row.tournamentStage ?? 1 })),
    tournamentStages: (value.tournamentStages ?? []).map(row => ({ name: row.name ?? '', format: row.format ?? 'SingleElimination',
      requiredWins: row.requiredWins ?? 1, advanceCount: row.advanceCount ?? 1, swissAdvanceWins: row.swissAdvanceWins ?? 3,
      swissEliminateLosses: row.swissEliminateLosses ?? 3, swissQualificationRequiredWins: row.swissQualificationRequiredWins ?? 2,
      swissEliminationRequiredWins: row.swissEliminationRequiredWins ?? 2 })) }
}
export function validSettings(value: LiveSoloSettingsDraft): boolean {
  return settingFields.every(field => Number.isInteger(value[field.key]) && value[field.key] >= field.min && value[field.key] <= field.max)
    && ['SingleElimination', 'DoubleElimination', 'Swiss'].includes(value.bracketFormat)
    && value.stageRules.every(row => ['Winners', 'Losers', 'GrandFinal', 'ResetFinal', 'Swiss'].includes(row.lane)
      && Number.isInteger(row.tournamentStage ?? 1) && (row.tournamentStage ?? 1) >= 1
      && Number.isInteger(row.stage) && row.stage >= 1 && Number.isInteger(row.requiredWins) && row.requiredWins >= 1 && row.requiredWins <= 1024)
    && new Set(value.stageRules.map(row => `${row.tournamentStage ?? 1}:${row.lane}:${row.stage}`)).size === value.stageRules.length
    && value.tournamentStages.every(row => ['SingleElimination', 'DoubleElimination', 'Swiss'].includes(row.format) && row.name.length <= 120
      && Number.isInteger(row.advanceCount) && row.advanceCount >= (row.format === 'Swiss' ? 0 : 1) && row.advanceCount <= 1024
      && [row.requiredWins, row.swissQualificationRequiredWins, row.swissEliminationRequiredWins].every(n => Number.isInteger(n) && n >= 1 && n <= 1024)
      && [row.swissAdvanceWins, row.swissEliminateLosses].every(n => Number.isInteger(n) && n >= 1 && n <= 32))
}
export function canManageLiveSolo(role: string | null | undefined) { return ['Administrator', 'Owner', 'Manager'].includes(role ?? '') }
