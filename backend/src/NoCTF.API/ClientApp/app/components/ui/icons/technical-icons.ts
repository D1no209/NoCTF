/** Names whose standalone files live in assets/svg/directions. CSS resolves each asset URL. */
export const technicalIcons = {
  puzzle: true, globe: true, key: true, chip: true, unwind: true, scan: true,
  radar: true, neural: true, phone: true, circuit: true, wireless: true,
  cloud: true, chain: true, target: true, flag: true,
} as const

export type TechnicalIconName = keyof typeof technicalIcons
