import { expect, test } from 'bun:test'
import { ProgramViewerLease, type ViewerOperation } from '../app/features/live-solo/program-viewer-lease'

test('programme setup does not admit a viewer; explicit entry and renewal reuse one session', async () => {
  const operations: ViewerOperation[] = [], changes: boolean[] = []
  const lease = new ProgramViewerLease(async action => { operations.push(action); return true }, active => changes.push(active))
  expect(await lease.renew()).toBe(false); expect(operations).toEqual([])
  expect(await lease.enter()).toBe(true); expect(await lease.enter()).toBe(false)
  expect(await lease.renew()).toBe(true); lease.leave()
  expect(operations).toEqual(['Enter', 'Renew', 'Leave']); expect(changes).toEqual([true, true, false])
})
test('failed renewal stops access and waits for explicit re-entry', async () => {
  const operations: ViewerOperation[] = []
  const lease = new ProgramViewerLease(async action => { operations.push(action); return action !== 'Renew' }, () => {})
  await lease.enter(); expect(await lease.renew()).toBe(false); expect(lease.active).toBe(false)
  await lease.renew(); expect(operations).toEqual(['Enter', 'Renew'])
  expect(await lease.enter()).toBe(true)
})
test('a late entry after navigation releases its slot without restoring playback', async () => {
  let finish!: (value: boolean) => void
  const operations: ViewerOperation[] = [], changes: boolean[] = []
  const lease = new ProgramViewerLease(action => { operations.push(action); return action === 'Enter' ? new Promise(resolve => { finish = resolve }) : Promise.resolve(true) }, active => changes.push(active))
  const entry = lease.enter(); lease.dispose(); finish(true)
  expect(await entry).toBe(false); expect(operations).toEqual(['Enter', 'Leave']); expect(changes).toEqual([false])
})
test('renewal finishing after exit never revives the player', async () => {
  let finish!: (value: boolean) => void
  const lease = new ProgramViewerLease(action => action === 'Renew' ? new Promise(resolve => { finish = resolve }) : Promise.resolve(true), () => {})
  await lease.enter(); const renewal = lease.renew(); lease.leave(); finish(true)
  expect(await renewal).toBe(false); expect(lease.active).toBe(false)
})
