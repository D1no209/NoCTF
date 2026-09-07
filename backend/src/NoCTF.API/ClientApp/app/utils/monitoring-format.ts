export function monitoringNumber(value?: number | null, digits = 2, suffix = ''): string {
  return value == null || !Number.isFinite(value) ? '—' : value.toLocaleString(undefined, { maximumFractionDigits: digits }) + suffix
}

export function monitoringMilliseconds(value?: number | null): string {
  return value == null || !Number.isFinite(value) ? '—' : `${monitoringNumber(value)} ms`
}

export function monitoringQuota(value: number | null | undefined, resource?: number): string {
  if (value == null || !Number.isFinite(value)) return '—'
  if (resource === 0) return `${monitoringNumber(value / 1024 / 1024)} MiB`
  if (resource === 1) return `${monitoringNumber(value / 1e9)} CPU`
  return monitoringNumber(value, 0)
}

export function monitoringQuotaPercent(available?: number | null, total?: number | null): number | null {
  if (available == null || total == null || !Number.isFinite(available) || !Number.isFinite(total) || total <= 0) return null
  return available / total * 100
}
