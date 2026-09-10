import { parseDateTime } from '@internationalized/date'

/** Keep the datetime-local contract: a wall-clock string, never a UTC conversion. */
export function normalizeLocalDateTime(value: string): string | null {
  const normalized = value.trim().replace(' ', 'T')
  if (!normalized) return ''
  if (!/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(?::\d{2}(?:\.\d{1,3})?)?$/.test(normalized)) return null
  try { parseDateTime(normalized); return normalized }
  catch { return null }
}

export function dateTimeWithDate(current: string, date: string): string {
  return `${date}T${normalizeLocalDateTime(current)?.split('T')[1] || '00:00'}`
}

export function dateTimeWithTime(current: string, hour: number, minute: number): string | null {
  if (!Number.isInteger(hour) || !Number.isInteger(minute) || hour < 0 || hour > 23 || minute < 0 || minute > 59) return null
  const day = normalizeLocalDateTime(current)?.split('T')[0]
  return day ? `${day}T${String(hour).padStart(2, '0')}:${String(minute).padStart(2, '0')}` : null
}
