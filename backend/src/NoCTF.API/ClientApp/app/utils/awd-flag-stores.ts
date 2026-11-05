import type { NoCtfapiEndpointsAdministrationChallengeBankFlagStoreContract as FlagStore } from '../api'

export const AWD_HISTORY_PRESERVING_COMMAND = "mkdir -p \"/var/lib/noctf/flags/${STORE_ID}\" && printf '%s\\n' \"${FLAG}\" > \"/var/lib/noctf/flags/${STORE_ID}/${FLAG_ID}\""

export function createAwdFlagStore(name = 'main', serviceName: string | null = 'main'): FlagStore {
  return { id: crypto.randomUUID(), name, delivery: 'RunnerCommand', targetServiceName: serviceName,
    injectionCommand: AWD_HISTORY_PRESERVING_COMMAND, injectionTimeoutSeconds: 30, preservesHistoricalFlags: true }
}
