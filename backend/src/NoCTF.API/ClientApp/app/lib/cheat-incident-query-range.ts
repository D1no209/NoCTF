import { translate } from '../utils/i18n'

const MaximumIncidentQueryRangeMilliseconds = 31 * 24 * 60 * 60 * 1000

export interface CheatIncidentQueryRange {
  from: string
  to: string
}

export interface CheatIncidentQueryRangeResolution {
  range: CheatIncidentQueryRange | null
  error: string | null
}

export function defaultCheatIncidentQueryRange(now = new Date()): CheatIncidentQueryRange {
  const to = now.getTime()
  return {
    from: new Date(to - MaximumIncidentQueryRangeMilliseconds).toISOString(),
    to: new Date(to).toISOString(),
  }
}

export function resolveCheatIncidentQueryRange(
  fromValue: string,
  toValue: string,
  now = new Date(),
): CheatIncidentQueryRangeResolution {
  const hasFrom = fromValue.trim().length > 0
  const hasTo = toValue.trim().length > 0

  if (!hasFrom && !hasTo)
    return { range: defaultCheatIncidentQueryRange(now), error: null }

  if (hasFrom !== hasTo) {
    return {
      range: null,
      error: translate("请同时填写起始和结束时间，或全部留空查询最近 31 天。"),
    }
  }

  const from = new Date(fromValue)
  const to = new Date(toValue)
  if (Number.isNaN(from.getTime()) || Number.isNaN(to.getTime()))
    return { range: null, error: translate("请输入有效的起始和结束时间。") }

  if (from > to)
    return { range: null, error: translate("起始时间不能晚于结束时间。") }

  if (to.getTime() - from.getTime() > MaximumIncidentQueryRangeMilliseconds)
    return { range: null, error: translate("查询时间范围不能超过 31 天。") }

  return {
    range: { from: from.toISOString(), to: to.toISOString() },
    error: null,
  }
}
