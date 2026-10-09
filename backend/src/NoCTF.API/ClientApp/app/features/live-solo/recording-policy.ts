import type { NoCtfapiEndpointsLiveSoloLiveSoloRecordingResponse as Recording, NoCtfDomainLiveSoloLiveSoloRecordingAction as Action } from '~/api'
import type { MessageKey } from '~/locales/en'
export const recordingActionKey = {
  Hold: 'liveSolo.recording.hold', ReleaseHold: 'liveSolo.recording.releaseHold', Publish: 'liveSolo.recording.publish', Withdraw: 'liveSolo.recording.withdraw',
  RetryPendingStart: 'liveSolo.recording.retryStart', ReconcileExport: 'liveSolo.recording.reconcile', RetryArchive: 'liveSolo.recording.retryArchive', StartNewChunk: 'liveSolo.recording.newChunk',
} satisfies Record<Action, MessageKey>
export function recordingActions(record: Recording | null, judge: boolean, publish: boolean): Action[] {
  if (!record || record.state === 'Deleting') return []
  const actions: Action[] = judge ? [record.disputeHold ? 'ReleaseHold' : 'Hold'] : []
  if (publish && (record.published || record.state === 'Completed')) actions.push(record.published ? 'Withdraw' : 'Publish')
  if (publish && record.state === 'RequiresReview') {
    if (record.failure === 'CapacityUnavailable') actions.push('RetryPendingStart')
    else if (record.failure === 'ArchiveCapacityUnavailable' || record.failure === 'ExportTooLarge') actions.push('RetryArchive')
    else actions.push('ReconcileExport')
  }
  if (publish && record.state === 'Failed' && record.failure === 'ExportFailed') actions.push('StartNewChunk')
  return actions
}
export function playableRecording(record: Recording | null) { return record?.state === 'Completed' && record.byteLength != null && record.byteLength > 0 }
export const recordingFailureKey = {
  CapacityUnavailable: 'liveSolo.recording.failure.capacity', StartUncertain: 'liveSolo.recording.failure.uncertain', ExportFailed: 'liveSolo.recording.failure.export',
  ExportTooLarge: 'liveSolo.recording.failure.size', ArchiveCapacityUnavailable: 'liveSolo.recording.failure.archiveCapacity',
} satisfies Record<import('~/api').NoCtfDomainLiveSoloLiveSoloRecordingFailure, MessageKey>
