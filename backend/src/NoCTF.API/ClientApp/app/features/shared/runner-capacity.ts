import type {
  NoCtfapiEndpointsRuntimeRunnerAdmissionFailureProtocol,
  NoCtfapiEndpointsRuntimeRunnerAdmissionStateProtocol,
  NoCtfapiEndpointsAdministrationPlatformRunnerResourceAmountResponse,
  NoCtfapiEndpointsAdministrationPlatformRunnerCapacitySnapshotResponse,
} from '../../api'
import type { MessageKey } from '../../locales/zh-CN'
import { translate } from '../../utils/i18n'

const failures = {
  NoEligibleRunner: 'capacity.noEligibleRunner',
  CpuBudgetInsufficient: 'capacity.cpuBudgetInsufficient',
  MemoryBudgetInsufficient: 'capacity.memoryBudgetInsufficient',
  PidBudgetInsufficient: 'capacity.pidBudgetInsufficient',
  NodePressureHigh: 'capacity.nodePressureHigh',
  ObservationStale: 'capacity.observationStale',
  LedgerRecovering: 'capacity.ledgerRecovering',
  StartupConcurrencyLimited: 'capacity.startupConcurrencyLimited',
  ProviderUnavailable: 'capacity.providerUnavailable',
  RequestExceedsNodeCapacity: 'capacity.requestExceedsNodeCapacity',
} satisfies Record<NoCtfapiEndpointsRuntimeRunnerAdmissionFailureProtocol, MessageKey>

const states = {
  Starting: 'capacity.starting', Reconciling: 'capacity.reconciling', Ready: 'capacity.ready',
  PressureBlocked: 'capacity.pressureBlocked', ProviderUnavailable: 'capacity.providerUnavailable', Draining: 'capacity.draining',
} satisfies Record<NoCtfapiEndpointsRuntimeRunnerAdmissionStateProtocol, MessageKey>

export function runnerFailureLabel(value?: NoCtfapiEndpointsRuntimeRunnerAdmissionFailureProtocol | null): string {
  return value ? translate(failures[value]) : ''
}

export function runnerStateLabel(value?: NoCtfapiEndpointsRuntimeRunnerAdmissionStateProtocol): string {
  return translate(value ? states[value] : 'ui.noSamples')
}

export function formatCapacityAmount(value?: NoCtfapiEndpointsAdministrationPlatformRunnerResourceAmountResponse | null): string {
  if (value?.nanoCpus == null || value.memoryBytes == null || value.pidsLimit == null) return '—'
  return `${(value.nanoCpus / 1_000_000_000).toFixed(3)} / ${(value.memoryBytes / 1_048_576).toFixed(0)} / ${value.pidsLimit}`
}

export function formatRunnerUsage(value: NoCtfapiEndpointsAdministrationPlatformRunnerCapacitySnapshotResponse): string {
  const observation = value.observation
  if (observation?.cpuUsageRatio == null || observation.memoryTotalBytes == null || observation.memoryAvailableBytes == null) return '—'
  return `${(observation.cpuUsageRatio * 100).toFixed(1)}% / ${((observation.memoryTotalBytes - observation.memoryAvailableBytes) / 1_048_576).toFixed(0)} / ${observation.pidsUsed ?? '—'}`
}
