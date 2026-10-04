import type { UiMessage } from '../utils/i18n'
import { translate } from '../utils/i18n'

const MaximumIncidentQueryRangeMilliseconds = 31 * 24 * 60 * 60 * 1000

export interface CheatIncidentQueryRange {
  from: string
  to: string
}

export interface CheatIncidentQueryRangeResolution {
  range: CheatIncidentQueryRange | null
  error: UiMessage | null
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
      error: translate("common.cheatIncident.description.fillBothStartEnd"),
    }
  }

  const from = new Date(fromValue)
  const to = new Date(toValue)
  if (Number.isNaN(from.getTime()) || Number.isNaN(to.getTime()))
    return { range: null, error: translate("common.cheatIncident.description.enterValidStartEnd") }

  if (from > to)
    return { range: null, error: translate("common.cheatIncident.validation.startTimeFormat") }

  if (to.getTime() - from.getTime() > MaximumIncidentQueryRangeMilliseconds)
    return { range: null, error: translate("common.cheatIncident.validation.queryTimeLength") }

  return {
    range: { from: from.toISOString(), to: to.toISOString() },
    error: null,
  }
}
