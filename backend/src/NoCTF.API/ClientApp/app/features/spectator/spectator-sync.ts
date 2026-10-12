export interface IdentifiedChange { id?: string; revision?: string; at?: string }

/** Bounded identity cache. Corrections update the baseline without replaying an old attack. */
export class SpectatorChangeWindow<T extends IdentifiedChange> {
  private seen = new Map<string, string>()
  private baseline = false
  reset() { this.seen.clear(); this.baseline = false }
  accept(changes: T[], _cutoff: string, live: boolean): T[] {
    const incoming = [...new Map(changes.filter(x => x.id).map(x => [x.id!, x])).values()].slice(0, 2000)
    const next = this.baseline && live ? incoming.filter(x => !this.seen.has(x.id!)) : []
    this.seen = new Map(incoming.map(change => [change.id!, change.revision ?? '']))
    this.baseline = live
    return next.reverse()
  }
}
