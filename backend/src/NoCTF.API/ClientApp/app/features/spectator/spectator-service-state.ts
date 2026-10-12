import type { NoCtfapiEndpointsCompetitionsSpectatorServiceResponse as Service } from '../../api'

export type SpectatorServiceState = 'Healthy' | 'Compromised' | 'Down' | 'Unknown'

/** Availability and this round's compromise are independent server facts. */
export function spectatorServiceState(service: Service | undefined): SpectatorServiceState {
  if (service?.health === 'Down') return 'Down'
  if (service?.isCompromised) return 'Compromised'
  return service?.health ?? 'Unknown'
}
