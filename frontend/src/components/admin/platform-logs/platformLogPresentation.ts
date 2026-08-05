import type {
  PlatformLog,
  PlatformLogLevel,
  PlatformLogService,
} from '@/api/platformLogs'

export const PLATFORM_LOG_HUB_PATH = '/hubs/v1/admin/platform-logs'
export const DEFAULT_PLATFORM_LOG_LEVEL = 3

export interface PlatformLogFilter {
  minimumLevel: PlatformLogLevel
  service: PlatformLogService | null
  from: string | null
  to: string | null
  category: string | null
  search: string | null
  competitionId: string | null
  runtimeInstanceId: string | null
  teamId: string | null
  userId: string | null
  competitionChallengeId: string | null
  submissionId: string | null
}

export function matchesPlatformLog(log: PlatformLog, filter: PlatformLogFilter) {
  const timestamp = log.timestamp ? Date.parse(log.timestamp) : Number.NaN
  return (log.level ?? -1) >= filter.minimumLevel
    && (filter.service === null || log.service === filter.service)
    && (!filter.from || timestamp >= Date.parse(filter.from))
    && (!filter.to || timestamp <= Date.parse(filter.to))
    && (!filter.category
      || log.category?.toLocaleLowerCase() === filter.category.toLocaleLowerCase())
    && matchesSearch(log, filter.search)
    && (!filter.competitionId || log.competitionId === filter.competitionId)
    && (!filter.runtimeInstanceId || log.runtimeInstanceId === filter.runtimeInstanceId)
    && (!filter.teamId || log.teamId === filter.teamId)
    && (!filter.userId || log.userId === filter.userId)
    && (!filter.competitionChallengeId || log.competitionChallengeId === filter.competitionChallengeId)
    && (!filter.submissionId || log.submissionId === filter.submissionId)
}

function matchesSearch(log: PlatformLog, search: string | null) {
  if (!search)
    return true
  const needle = search.toLocaleLowerCase()
  return [
    log.category,
    log.eventName,
    log.message,
    log.exceptionType,
    log.exceptionMessage,
  ].some(value => value?.toLocaleLowerCase().includes(needle))
}

export function platformLogLevelName(level?: number) {
  return ['Trace', 'Debug', 'Information', 'Warning', 'Error', 'Critical'][level ?? -1]
    ?? 'Unknown'
}

export function platformLogServiceName(service?: number) {
  return ['API', 'Worker', 'Runner'][service ?? -1] ?? 'Unknown'
}
