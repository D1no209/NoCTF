import type { TrendSeries } from './types'

const MINIMUM_RANGE_MS = 60_000
const EDGE_PADDING_RATIO = 0.015

function timestamp(value?: Date | string | null): number | null {
  if (!value) return null
  const parsed = new Date(value).getTime()
  return Number.isFinite(parsed) ? parsed : null
}

export function scoreTrendTimeRange(
  series: TrendSeries[],
  rangeStart?: string | null,
  rangeEnd?: string | null,
  fallbackNow = Date.now(),
) {
  const candidates = [timestamp(rangeStart), timestamp(rangeEnd)]
  for (const team of series) {
    for (const point of team.points ?? [])
      candidates.push(timestamp(point.at))
  }

  const valid = candidates.filter((value): value is number => value !== null)
  let start = valid.length ? Math.min(...valid) : fallbackNow
  let end = valid.length ? Math.max(...valid) : fallbackNow + MINIMUM_RANGE_MS
  if (end - start < MINIMUM_RANGE_MS)
    end = start + MINIMUM_RANGE_MS

  const padding = Math.max(MINIMUM_RANGE_MS, Math.round((end - start) * EDGE_PADDING_RATIO))
  return {
    start,
    end,
    axisMin: start - padding,
    axisMax: end + padding,
  }
}
