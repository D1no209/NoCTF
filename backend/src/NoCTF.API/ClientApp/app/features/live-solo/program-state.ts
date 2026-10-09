export interface ProgramSegmentState<T> { id?: string; state?: T }
/** Only the segment actually being played supplies score/round state. A latest-state fallback is forbidden. */
export function stateForPlayingSegment<T>(segments: readonly ProgramSegmentState<T>[], id: string | null): T | null {
  return id ? segments.find(segment => segment.id === id)?.state ?? null : null
}
export function rememberPublishedStates<T>(known: ReadonlyMap<string,T>, segments: readonly ProgramSegmentState<T>[], playingId: string | null, limit=500): ReadonlyMap<string,T> {
  const next = new Map(known)
  for (const segment of segments) if (segment.id && segment.state !== undefined && !next.has(segment.id)) next.set(segment.id, segment.state)
  for (const id of next.keys()) { if (next.size <= limit) break; if (id !== playingId) next.delete(id) }
  return next
}
export function segmentIdFromUrl(url: string, base: string): string | null {
  const parsed = new URL(url, base), origin = new URL(base).origin
  return parsed.origin === origin ? /\/program\/segments\/([\da-f-]{36})$/i.exec(parsed.pathname)?.[1] ?? null : null
}
