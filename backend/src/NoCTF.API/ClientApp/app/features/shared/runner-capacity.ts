import type {
  NoCtfapiEndpointsRuntimeRunnerAdmissionFailureProtocol,
  NoCtfapiEndpointsAdministrationRuntimeAdminRuntimeResourceAmountResponse,
} from '../../api'
import type { MessageKey } from '../../locales/zh-CN'
import { translate } from '../../utils/i18n'

const failures = {
  NoEligibleRunner: 'capacity.noEligibleRunner',
  CpuActualCapacityInsufficient: 'capacity.cpuActualCapacityInsufficient',
  MemoryActualCapacityInsufficient: 'capacity.memoryActualCapacityInsufficient',
  PidActualCapacityInsufficient: 'capacity.pidActualCapacityInsufficient',
  NodePressureHigh: 'capacity.nodePressureHigh',
  ObservationStale: 'capacity.observationStale',
  LedgerRecovering: 'capacity.ledgerRecovering',
  StartupConcurrencyLimited: 'capacity.startupConcurrencyLimited',
  ProviderUnavailable: 'capacity.providerUnavailable',
  RequestExceedsNodeCapacity: 'capacity.requestExceedsNodeCapacity',
} satisfies Record<NoCtfapiEndpointsRuntimeRunnerAdmissionFailureProtocol, MessageKey>

export function runnerFailureLabel(value?: NoCtfapiEndpointsRuntimeRunnerAdmissionFailureProtocol | null): string {
  return value ? translate(failures[value]) : ''
}

export function formatCapacityAmount(
  value?: NoCtfapiEndpointsAdministrationRuntimeAdminRuntimeResourceAmountResponse | null,
): string {
  if (value?.cpuMillicores == null || value.memoryBytes == null) return '—'
  return `${(value.cpuMillicores / 1000).toFixed(3)} / ${(value.memoryBytes / 1_048_576).toFixed(0)} / ${value.pidsLimit ?? '—'}`
}
