import type {
  PlatformAuditLog,
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
  competitionId: string | null
  runtimeInstanceId: string | null
}

export function matchesPlatformLog(log: PlatformLog, filter: PlatformLogFilter) {
  const timestamp = log.timestamp ? Date.parse(log.timestamp) : Number.NaN
  return (log.level ?? -1) >= filter.minimumLevel
    && (filter.service === null || log.service === filter.service)
    && (!filter.from || timestamp >= Date.parse(filter.from))
    && (!filter.to || timestamp <= Date.parse(filter.to))
    && (!filter.competitionId || log.competitionId === filter.competitionId)
    && (!filter.runtimeInstanceId || log.runtimeInstanceId === filter.runtimeInstanceId)
}

export function platformLogLevelName(level?: number) {
  return ['Trace', 'Debug', 'Information', 'Warning', 'Error', 'Critical'][level ?? -1]
    ?? 'Unknown'
}

export function platformLogServiceName(service?: number) {
  return ['API', 'Worker', 'Runner'][service ?? -1] ?? 'Unknown'
}

export function platformAuditKindName(audit: PlatformAuditLog) {
  if (audit.kind === 0)
    return 'Competition'
  if (audit.kind === 1)
    return 'User account'
  return 'Unknown'
}

export function platformAuditActionName(audit: PlatformAuditLog) {
  if (audit.kind === 0) {
    const statuses = ['Draft', 'Visible', 'Published', 'Running', 'Paused', 'Finished']
    return `${statuses[audit.fromCompetitionStatus ?? -1] ?? 'Unknown'} → ${statuses[audit.toCompetitionStatus ?? -1] ?? 'Unknown'}`
  }
  return ['Banned', 'Disabled', 'Anonymized', 'Physically deleted'][audit.userAccountAction ?? -1]
    ?? 'Unknown'
}
