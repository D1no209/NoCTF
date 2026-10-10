import type { NoCtfapiEndpointsCompetitionsGameModeProtocol as GameMode } from '../api'

const gameModeLabels = {
  Ctf: 'CTF',
  Awd: 'AWD',
  Awdp: 'AWDP',
  Koh: 'KoH',
  LiveSolo: 'LiveSolo',
} satisfies Record<GameMode, string>

export const gameModeOptions = (Object.keys(gameModeLabels) as GameMode[])
  .map(value => ({ value, label: gameModeLabels[value] }))

export function isGameMode(value: unknown): value is GameMode {
  return typeof value === 'string' && Object.hasOwn(gameModeLabels, value)
}
