import { describe, expect, test } from 'bun:test'
import { dateTimeWithDate, dateTimeWithTime, normalizeLocalDateTime } from '../app/components/ui/date-time-picker/date-time'
import { constraintFeedback } from '../app/components/ui/form/validation'

describe('local date and time field', () => {
  test('preserves wall-clock values, seconds and milliseconds without timezone conversion', () => {
    expect(normalizeLocalDateTime('2026-09-09 14:30')).toBe('2026-09-09T14:30')
    expect(normalizeLocalDateTime('2026-09-09T14:30:45.123')).toBe('2026-09-09T14:30:45.123')
    expect(dateTimeWithDate('2026-09-09T14:30:45', '2026-10-01')).toBe('2026-10-01T14:30:45')
  })
  test('rejects impossible or incomplete dates and supports explicit clearing', () => {
    for (const value of ['2026-02-30T12:00', '2026-09', '2026-09-09T25:00', '2026-09-09T12:61', '2026-09-09T12:00Z'])
      expect(normalizeLocalDateTime(value)).toBeNull()
    expect(normalizeLocalDateTime('')).toBe('')
    expect(normalizeLocalDateTime('2024-02-29T12:00')).toBe('2024-02-29T12:00')
  })
  test('updates only the selected time part and requires a selected day', () => {
    expect(dateTimeWithTime('2026-09-09T14:30', 2, 5)).toBe('2026-09-09T02:05')
    expect(dateTimeWithTime('', 2, 5)).toBeNull()
    expect(dateTimeWithTime('2026-09-09T14:30', -1, 5)).toBeNull()
  })
})

describe('custom constraint feedback', () => {
  const valid = { valid: true, valueMissing: false, typeMismatch: false, tooShort: false, tooLong: false, rangeUnderflow: false, rangeOverflow: false, stepMismatch: false, patternMismatch: false, customError: false, badInput: false }
  test('keeps valid fields quiet and prioritizes required fields over format errors', () => {
    expect(constraintFeedback({ label: 'Name', validity: valid })).toBeNull()
    expect(constraintFeedback({ label: 'Name', validity: { ...valid, valid: false, valueMissing: true, typeMismatch: true } }))
      .toEqual({ key: 'form.required', values: { field: 'Name' } })
  })
  test('carries field context and bounds into localized messages', () => {
    expect(constraintFeedback({ label: 'Name', minLength: 3, validity: { ...valid, valid: false, tooShort: true } }))
      .toEqual({ key: 'form.tooShort', values: { field: 'Name', count: 3 } })
    expect(constraintFeedback({ label: 'Count', min: '2', validity: { ...valid, valid: false, rangeUnderflow: true } }))
      .toEqual({ key: 'form.minimum', values: { field: 'Count', value: '2' } })
  })
})
