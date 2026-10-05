import type {
  NoCtfapiEndpointsAdministrationGameplayFactsHistoricalAdjudicationDifferenceItemResponse as PreviewItem,
  NoCtfapiEndpointsAdministrationGameplayFactsAdjudicationFindingSeverityProtocol as Severity,
  NoCtfapiEndpointsAdministrationGameplayFactsAdjudicationFindingClassificationProtocol as Classification,
  NoCtfapiEndpointsAdministrationGameplayFactsAdjudicationEvidenceCompletenessProtocol as Completeness,
  NoCtfapiEndpointsCompetitionsEventsCompetitionEventKindProtocol as EventKind,
} from '../../api'
import type { MessageKey } from '../../locales/zh-CN'
import { translate } from '../../utils/i18n'

const severityKeys = { Information: 'adjudication.information', Warning: 'adjudication.review', Error: 'adjudication.anomaly' } satisfies Record<Severity, MessageKey>
const classifications = {
  CurrentResultMismatch: 'adjudication.currentMismatch', IntegrityGap: 'adjudication.integrityGap',
  SuspectedDuplicate: 'adjudication.duplicateEvent', LegalHistoryChange: 'adjudication.legalChange',
  EligibilityAdjustment: 'adjudication.eligibilityAdjustment', InsufficientEvidence: 'adjudication.insufficientEvidence',
  RetainedResult: 'adjudication.retainedResult',
} satisfies Record<Classification, MessageKey>
const completenessKeys = {
  Complete: 'adjudication.complete', Truncated: 'adjudication.truncated',
  MissingFields: 'adjudication.missingFields', Ambiguous: 'adjudication.ambiguous',
} satisfies Record<Completeness, MessageKey>
const eventKeys: Partial<Record<EventKind, MessageKey>> = {
  GameplayFactReceived: 'adjudication.received', GameplayFactAdjudicated: 'adjudication.adjudicated',
  ScoringRecorded: 'adjudication.resultRecorded', FirstBloodAwarded: 'common.label.firstBlood',
  SecondBloodAwarded: 'common.label.secondBlood', ThirdBloodAwarded: 'common.label.thirdBlood',
  TrackConfigurationUpdated: 'adjudication.trackChanged', TeamTrackChanged: 'adjudication.teamTrackChanged',
  TeamBanned: 'adjudication.teamBanned', TeamUnbanned: 'adjudication.teamUnbanned',
  TeamDeleted: 'adjudication.teamDeleted', TeamRegistrationChanged: 'adjudication.registrationChanged',
  TeamBanAppealAccepted: 'adjudication.banCorrected', TeamBanCorrectionPublished: 'adjudication.banCorrected',
}

export function adjudicationSeverity(item: PreviewItem): Severity {
  if (item.differences?.some(finding => finding.severity === 'Error')) return 'Error'
  if (!item.differences?.length || item.differences.some(finding => finding.severity !== 'Information')) return 'Warning'
  return 'Information'
}

export function adjudicationCounts(items: PreviewItem[]): Record<Severity, number> {
  return items.reduce((counts, item) => { counts[adjudicationSeverity(item)]++; return counts }, { Information: 0, Warning: 0, Error: 0 })
}

export function adjudicationSeverityLabel(value?: Severity): string { return translate(severityKeys[value ?? 'Warning']) }
export function adjudicationClassificationLabel(value?: Classification): string { return translate(classifications[value ?? 'InsufficientEvidence']) }
export function adjudicationCompletenessLabel(value?: Completeness): string { return translate(completenessKeys[value ?? 'MissingFields']) }
export function adjudicationEventLabel(value?: EventKind): string { return translate(value ? eventKeys[value] ?? 'adjudication.event' : 'adjudication.event') }
export function adjudicationVariant(value?: Severity): 'destructive' | 'outline' | 'secondary' {
  return value === 'Error' ? 'destructive' : value === 'Information' ? 'secondary' : 'outline'
}
