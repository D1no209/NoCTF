export interface CommandAwdService {
  teamId: string
  teamName: string
  challengeId: string
  challengeName: string
  status: 'healthy' | 'down' | 'unknown'
}

export interface CommandAwdAttack {
  id: string
  attackerTeamName: string
  victimTeamName: string
  challengeName: string
  roundNumber: number
  timestamp: string
}

export interface CommandAwdChallenge {
  id: string
  title: string
}
