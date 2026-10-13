export interface ControlObservation {
  id?: string; at?: string; challengeId?: string; controllerId?: string | null; kind?: string
}
export interface ControlInterval { start: number; end: number; controllerId: string | null; unknown: boolean }

/** Intervals describe confirmed samples, never browser-clock accrual. Before the
 * first available observation the state is explicitly unknown. */
export function controlIntervals(events: ControlObservation[], start: number, end: number): ControlInterval[] {
  if (!(end > start)) return []
  const ordered = [...events].filter(e => Date.parse(e.at ?? '') <= end)
    .sort((a, b) => Date.parse(a.at!) - Date.parse(b.at!) || (a.id ?? '').localeCompare(b.id ?? ''))
  const result: ControlInterval[] = []
  let current: ControlInterval = { start, end, controllerId: null, unknown: true }
  for (const event of ordered) {
    const at = Math.max(start, Date.parse(event.at!))
    const controllerId = event.controllerId ?? null, unknown = event.kind === 'DetectionFailed'
    if (controllerId === current.controllerId && unknown === current.unknown) continue
    if (at > current.start) result.push({ ...current, end: at })
    current = { start: at, end, controllerId, unknown }
  }
  if (current.start < end) result.push(current)
  return result
}
