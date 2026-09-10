export const statusIcons = {
  solved: true,
  'attack-success': true,
  'defense-success': true,
  'attack-defense-success': true,
} as const

export type StatusIconName = keyof typeof statusIcons
