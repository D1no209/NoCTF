import { describe, expect, test } from 'bun:test'
import { PostgamePreviewLease } from '../app/features/live-solo/postgame-preview-lease'

function fixture() {
  const urls: Array<string | null> = [], failures: unknown[] = [], timers = new Map<number, () => void>()
  const delays: number[] = []
  let timer = 0
  const lease = new PostgamePreviewLease({changed:url=>urls.push(url),failed:cause=>failures.push(cause),schedule:(run,delay)=>{
    delays.push(delay); const id=++timer; timers.set(id,run); return ()=>{timers.delete(id)}
  }})
  return {lease,urls,failures,timers,delays}
}
describe('LiveSolo postgame PDF browser handoff',()=>{
  test('renews before expiry without changing the loaded PDF source and stops on departure',async()=>{
    const f=fixture();let calls=0
    await f.lease.open(async()=>{calls++;return '/same-scoped-pdf'})
    expect(f.delays).toEqual([240_000]);expect(f.urls.at(-1)).toBe('/same-scoped-pdf')
    const renew=[...f.timers.values()][0]!;f.timers.clear();renew();await Promise.resolve();await Promise.resolve()
    expect(calls).toBe(2);expect(f.urls.at(-1)).toBe('/same-scoped-pdf');expect(f.timers.size).toBe(1)
    f.lease.close();expect(f.timers.size).toBe(0);expect(f.urls.at(-1)).toBeNull()
  })
  test('a previous scope response cannot restore a closed preview or cancel its successor',async()=>{
    const f=fixture();let complete!: (value:string)=>void;let oldSignal!:AbortSignal
    const old=f.lease.open(signal=>{oldSignal=signal;return new Promise(resolve=>{complete=resolve})})
    await f.lease.open(async()=>'/new-scope')
    expect(oldSignal.aborted).toBe(true);complete('/old-scope');await old
    expect(f.urls.at(-1)).toBe('/new-scope');expect(f.timers.size).toBe(1)
    f.lease.close()
  })
  test('revoked or failed renewal removes access and does not create a retry loop',async()=>{
    const f=fixture();let calls=0
    await f.lease.open(async()=>{if(++calls>1)throw new Error('access withdrawn');return '/pdf'})
    const renew=[...f.timers.values()][0]!;f.timers.clear();renew();await Promise.resolve();await Promise.resolve()
    expect(f.urls.at(-1)).toBeNull();expect(f.failures).toHaveLength(1);expect(f.timers.size).toBe(0);expect(calls).toBe(2)
  })
  test('unmount aborts an in-flight grant without presenting a cancellation error',async()=>{
    const f=fixture();let reject!: (cause:unknown)=>void;let signal!:AbortSignal
    const pending=f.lease.open(value=>{signal=value;return new Promise((_,fail)=>{reject=fail})})
    f.lease.close();reject(new Error('aborted'));await pending
    expect(signal.aborted).toBe(true);expect(f.failures).toEqual([]);expect(f.timers.size).toBe(0)
  })
})
