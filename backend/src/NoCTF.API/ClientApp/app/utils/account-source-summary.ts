import { dateTimestamp } from './date-value'
type SourceActivity = { ipAddress?: string | null, occurredAt?: Date | string | null }
export type AccountSourceSummary = { address: string, count: number, lastSeen: Date | string | null }

/** A bounded summary of the returned recent sample, not an all-time login or cheating assessment. */
export function summarizeAccountSources(activities: readonly SourceActivity[]): AccountSourceSummary[] {
  const sources = new Map<string, AccountSourceSummary>()
  for (const activity of activities) {
    const address = activity.ipAddress?.trim().toLowerCase()
    if (!address) continue
    const time = activity.occurredAt && Number.isFinite(dateTimestamp(activity.occurredAt)) ? activity.occurredAt : null
    const existing = sources.get(address)
    if (!existing) sources.set(address, { address, count: 1, lastSeen: time })
    else {
      existing.count++
      if (time && (!existing.lastSeen || dateTimestamp(time) > dateTimestamp(existing.lastSeen))) existing.lastSeen = time
    }
  }
  return [...sources.values()].sort((left, right) => right.count - left.count
    || (dateTimestamp(right.lastSeen ?? '') || 0) - (dateTimestamp(left.lastSeen ?? '') || 0)
    || left.address.localeCompare(right.address)).slice(0, 3)
}
