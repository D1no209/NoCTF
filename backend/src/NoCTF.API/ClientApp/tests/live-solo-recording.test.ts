import { describe, expect, test } from 'bun:test'
import { playableRecording, recordingActions } from '../app/features/live-solo/recording-policy'
import type { NoCtfapiEndpointsLiveSoloLiveSoloRecordingResponse as Recording } from '../app/api'
const ready: Recording = { state: 'Completed', byteLength: 1234, disputeHold: false, published: false }
describe('LiveSolo recording permissions and readiness', () => {
  test('observers only read; judges hold; managers publish', () => {
    expect(recordingActions(ready, false, false)).toEqual([])
    expect(recordingActions(ready, true, false)).toEqual(['Hold'])
    expect(recordingActions(ready, true, true)).toEqual(['Hold', 'Publish'])
    expect(recordingActions({ ...ready, disputeHold: true, published: true }, true, true)).toEqual(['ReleaseHold', 'Withdraw'])
  })
  test('deleting recordings cannot gain a late hold or be published', () => {
    expect(recordingActions({ ...ready, state: 'Deleting' }, true, true)).toEqual([])
    expect(recordingActions({ ...ready, state: 'Finalizing' }, true, true)).toEqual(['Hold'])
  })
  test('preview and native download require a completed nonempty stored file', () => {
    expect(playableRecording(ready)).toBe(true)
    expect(playableRecording({ ...ready, state: 'RequiresReview' })).toBe(false)
    expect(playableRecording({ ...ready, byteLength: 0 })).toBe(false)
    expect(playableRecording(null)).toBe(false)
  })
  test('recovery is manager-only and selects actions from explicit failures', () => {
    const pending: Recording={state:'RequiresReview',failure:'CapacityUnavailable'}
    expect(recordingActions(pending,true,false)).toEqual(['Hold'])
    expect(recordingActions(pending,true,true)).toEqual(['Hold','RetryPendingStart'])
    expect(recordingActions({...pending,failure:'StartUncertain'},true,true)).toEqual(['Hold','ReconcileExport'])
    expect(recordingActions({...pending,failure:'ArchiveCapacityUnavailable'},true,true)).toEqual(['Hold','RetryArchive'])
    expect(recordingActions({state:'Failed',failure:'ExportFailed'},true,true)).toEqual(['Hold','StartNewChunk'])
    expect(playableRecording({...ready,state:'Failed',failure:'ExportFailed'})).toBe(false)
  })
})
