import type { NoCtfapiEndpointsCompetitionsSpectatorServiceResponse as Service, NoCtfDomainCompetitionsAwdAwdHealth } from '../../api'

export type SpectatorServiceState = NoCtfDomainCompetitionsAwdAwdHealth | 'Unknown' | 'Pending' | 'Running' | 'Disabled'

/** The lamp describes current health or execution only; captures have their own marker. */
export function spectatorServiceState(service: Service | undefined): SpectatorServiceState {
  if (service?.enabled === false) return 'Disabled'
  if (service?.health) return service.health
  if (service?.execution === 'Pending' || service?.execution === 'Running') return service.execution
  return 'Unknown'
}
