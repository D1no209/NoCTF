import { expect, test } from 'bun:test'
import { monitoringNumber, monitoringMilliseconds, monitoringQuota, monitoringQuotaPercent } from '../app/utils/monitoring-format'

test('monitoring distinguishes missing values from zero and does not convert milliseconds again', () => {
  expect(monitoringMilliseconds(null)).toBe('—')
  expect(monitoringNumber(null, 2, '%')).toBe('—')
  expect(monitoringNumber(0, 2, '%')).toBe('0%')
  expect(monitoringMilliseconds(Number.NaN)).toBe('—')
  expect(monitoringMilliseconds(0)).toBe('0 ms')
  expect(monitoringMilliseconds(12)).toBe('12 ms')
  expect(monitoringQuota(1024 * 1024, 0)).toBe('1 MiB')
  expect(monitoringQuota(2e9, 1)).toBe('2 CPU')
  expect(monitoringQuotaPercent(0, 0)).toBeNull()
  expect(monitoringQuotaPercent(null, 10)).toBeNull()
  expect(monitoringQuotaPercent(0, 10)).toBe(0)
  expect(monitoringQuotaPercent(4, 8)).toBe(50)
})
