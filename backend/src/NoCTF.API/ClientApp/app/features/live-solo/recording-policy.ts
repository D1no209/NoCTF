import type { NoCtfapiEndpointsLiveSoloLiveSoloRecordingResponse as Recording, NoCtfDomainLiveSoloLiveSoloRecordingAction as Action } from '~/api'
import type { MessageKey } from '~/locales/en'
export const recordingActionKey = {
  Hold: 'liveSolo.recording.hold', ReleaseHold: 'liveSolo.recording.releaseHold', Publish: 'liveSolo.recording.publish', Withdraw: 'liveSolo.recording.withdraw',
} satisfies Record<Action, MessageKey>
export function recordingActions(record: Recording | null, judge: boolean, publish: boolean): Action[] {
  if (!record || record.state === 'Deleting') return []
  const actions: Action[] = judge ? [record.disputeHold ? 'ReleaseHold' : 'Hold'] : []
  if (publish && (record.published || record.state === 'Completed')) actions.push(record.published ? 'Withdraw' : 'Publish')
  return actions
}
export function playableRecording(record: Recording | null) { return record?.state === 'Completed' && record.byteLength != null && record.byteLength > 0 }
