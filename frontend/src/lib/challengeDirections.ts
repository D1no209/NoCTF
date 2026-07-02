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
] as const

export function normalizeDirection(value?: string | null) {
  return (value ?? 'MISC').trim().toUpperCase()
}
