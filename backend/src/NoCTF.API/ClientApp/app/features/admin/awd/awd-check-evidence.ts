import type { NoCtfapiEndpointsAdministrationGameplayFactsAwdCheckEvidenceResponse as Evidence, NoCtfDomainCompetitionsAwdAwdHealth, NoCtfDomainCompetitionsAwdAwdProbeOutcome, NoCtfDomainCompetitionsAwdAwdCheckExecution, NoCtfDomainCompetitionsAwdAwdSettlementState, NoCtfDomainCompetitionsAwdAwdFlagObligation, NoCtfDomainCompetitionsAwdAwdCheckReason } from '../../../api'
import type { MessageKey } from '../../../locales/en'
import { translate } from '../../../utils/i18n'

type CheckValue = NoCtfDomainCompetitionsAwdAwdHealth | NoCtfDomainCompetitionsAwdAwdProbeOutcome | NoCtfDomainCompetitionsAwdAwdCheckExecution | NoCtfDomainCompetitionsAwdAwdSettlementState | NoCtfDomainCompetitionsAwdAwdFlagObligation | NoCtfDomainCompetitionsAwdAwdCheckReason
const labels = {
  Ok: 'administration.awd.check.Ok',
  Mumble: 'administration.awd.check.Mumble',
  Offline: 'administration.awd.check.Offline',
  Recovering: 'administration.awd.check.Recovering',
  InternalError: 'administration.awd.check.InternalError',
  Unknown: 'administration.awd.check.Unknown',
  Pending: 'administration.awd.check.Pending',
  Running: 'administration.awd.check.Running',
  Completed: 'administration.awd.check.Completed',
  FailedToDetermine: 'administration.awd.check.FailedToDetermine',
  Provisional: 'administration.awd.check.Provisional',
  Final: 'administration.awd.check.Final',
  PendingAdjudication: 'administration.awd.check.PendingAdjudication',
  Exempted: 'administration.awd.check.Exempted',
  Passed: 'administration.awd.check.Passed',
  TargetFailure: 'administration.awd.check.TargetFailure',
  PlatformFailure: 'administration.awd.check.PlatformFailure',
  NotRequired: 'administration.awd.check.NotRequired',
  PlatformPending: 'administration.awd.check.PlatformPending',
  PlayerRequired: 'administration.awd.check.PlayerRequired',
  None: 'administration.awd.check.None',
  ConnectionRefused: 'administration.awd.check.ConnectionRefused',
  ConnectTimeout: 'administration.awd.check.ConnectTimeout',
  RequestTimeout: 'administration.awd.check.RequestTimeout',
  FunctionalCheckFailed: 'administration.awd.check.FunctionalCheckFailed',
  FlagStoreFailed: 'administration.awd.check.FlagStoreFailed',
  CurrentFlagMissing: 'administration.awd.check.CurrentFlagMissing',
  HistoricalFlagMissing: 'administration.awd.check.HistoricalFlagMissing',
  CheckerCrashed: 'administration.awd.check.CheckerCrashed',
  CheckerTimedOut: 'administration.awd.check.CheckerTimedOut',
  TaskNotExecuted: 'administration.awd.check.TaskNotExecuted',
  CallbackMissing: 'administration.awd.check.CallbackMissing',
  InfrastructureFailure: 'administration.awd.check.InfrastructureFailure',
  IncompleteEvidence: 'administration.awd.check.IncompleteEvidence',
  InvalidEvidence: 'administration.awd.check.InvalidEvidence',
  RuntimeSuperseded: 'administration.awd.check.RuntimeSuperseded',
  LateReceipt: 'administration.awd.check.LateReceipt',
} satisfies Record<CheckValue, MessageKey>
export function awdCheckLabel(value?: CheckValue | null) { return translate(labels[value ?? 'Unknown']) }

/** Only a prerequisite for enabling review. The server evaluates the saved manifest and probes. */
export function awdEvidenceCanBeReviewed(evidence: Evidence | null): boolean {
  if (!evidence?.manifestComplete || !evidence.evidenceComplete || !evidence.attemptId) return false
  const [start, deadline, observed, received] = [evidence.attemptStartedAt, evidence.attemptDeadline, evidence.observedAt, evidence.receivedAt]
    .map(value => value ? Date.parse(value) : NaN)
  return [start, deadline, observed, received].every(Number.isFinite)
    && observed! >= start! && observed! <= deadline! && observed! <= received!
}
