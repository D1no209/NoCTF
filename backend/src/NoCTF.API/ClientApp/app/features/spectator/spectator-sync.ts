export interface IdentifiedChange { id?: string; revision?: string; at?: string }

/** Bounded identity cache. Corrections update the baseline without replaying an old attack. */
export class SpectatorChangeWindow<T extends IdentifiedChange> {
  private seen = new Map<string, string>()
  private baseline = false
  reset() { this.seen.clear(); this.baseline = false }
  accept(changes: T[], _cutoff: string, live: boolean): T[] {
    const incoming = [...new Map(changes.filter(x => x.id).map(x => [x.id!, x])).values()].slice(0, 2000)
    const next = this.baseline && live ? incoming.filter(x => !this.seen.has(x.id!)) : []
    // Keep one extra window of tombstones: retracting and restoring a corrected
    // fact must not turn it into a newly announced attack. Never evict a fact
    // still present in the current window when trimming the identity cache.
    for (const change of [...incoming].reverse()) this.seen.set(change.id!, change.revision ?? '')
    const currentIds = new Set(incoming.map(change => change.id!))
    for (const id of this.seen.keys()) {
      if (this.seen.size <= 4000) break
      if (!currentIds.has(id)) this.seen.delete(id)
    }
    this.baseline = live
    return next.reverse()
  }
}
