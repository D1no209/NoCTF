import { expect, test } from 'bun:test'
import { formatCapacityAmount, formatRunnerUsage, runnerFailureLabel, runnerStateLabel } from '../app/features/shared/runner-capacity'

test('capacity amounts retain a negative deficit and distinguish unavailable data from zero', () => {
  expect(formatCapacityAmount(null)).toBe('—')
  expect(formatCapacityAmount({ nanoCpus: 0, memoryBytes: 0, pidsLimit: 0 })).toBe('0.000 / 0 / 0')
  expect(formatCapacityAmount({ nanoCpus: -250000000, memoryBytes: -1048576, pidsLimit: -1 })).toBe('-0.250 / -1 / -1')
})

test('node pressure and accounting shortages have distinct explanations', () => {
  expect(runnerFailureLabel('CpuBudgetInsufficient')).toContain('CPU')
  expect(runnerFailureLabel('ObservationStale')).toContain('观测')
  expect(runnerFailureLabel('LedgerRecovering')).toContain('账本')
  expect(runnerStateLabel('Ready')).toContain('接单')
  expect(runnerFailureLabel(null)).toBe('')
})

test('unknown Kubernetes PID usage remains unknown', () => {
  expect(formatRunnerUsage({ observation: { cpuUsageRatio: 0.25, memoryTotalBytes: 2097152, memoryAvailableBytes: 1048576, pidsUsed: null } }))
    .toBe('25.0% / 1 / —')
  expect(formatRunnerUsage({})).toBe('—')
})
