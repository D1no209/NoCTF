export const challengeDirections = [
  'WEB',
  'PWN',
  'MISC',
  'REVERSE',
  'MOBILE',
  'CRYPTO',
  'FORENSICS',
  'AI',
  'BLOCKCHAIN',
  'HARDWARE',
  'OSINT',
  'CLOUD',
  'UNCATEGORIZED',
] as const

export function normalizeDirection(value?: string | null) {
  return (value ?? 'UNCATEGORIZED').trim().toUpperCase()
}

export function challengeDirectionsForType(typeId?: string | null): readonly string[] {
  switch ((typeId ?? '').trim().toLowerCase()) {
    case 'awd':
      return ['WEB', 'PWN']
    case 'awdp':
      return ['WEB', 'PWN', 'AI']
    default:
      return challengeDirections.filter(direction => direction !== 'UNCATEGORIZED')
  }
}
