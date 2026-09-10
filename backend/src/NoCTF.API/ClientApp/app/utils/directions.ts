import {
  Binary,
  Bot,
  Bug,
  CircuitBoard,
  Cloud,
  Cpu,
  Flag,
  Globe,
  KeyRound,
  Link2,
  Puzzle,
  ScanSearch,
  Search,
  Smartphone,
} from '@lucide/vue'
import type { FunctionalComponent } from 'vue'
import type { TechnicalIconName } from '../components/ui/icons/technical-icons'

interface DirectionStyle {
  icon: FunctionalComponent
  glyph: TechnicalIconName
  /** 图标/标题等着色 */
  text: string
  /** 方向徽章配色(描边 + 浅底 + 着色文字) */
  badge: string
}

const fallback: DirectionStyle = {
  icon: Flag, glyph: 'flag',
  text: 'text-muted-foreground',
  badge: 'border-border bg-muted/50 text-muted-foreground',
}

/**
 * 题目方向 → 图标 + 颜色的唯一映射表。
 * direction 是开放文本,按小写前缀/包含关系匹配,未知名称走 fallback。
 */
const directionStyles: Record<string, DirectionStyle> = {
  web: { icon: Globe, glyph: 'globe', text: 'text-blue-600 dark:text-blue-400', badge: 'border-blue-500/40 bg-blue-500/10 text-blue-700 dark:text-blue-300' },
  pwn: { icon: Bug, glyph: 'chip', text: 'text-red-600 dark:text-red-400', badge: 'border-red-500/40 bg-red-500/10 text-red-700 dark:text-red-300' },
  reverse: { icon: Binary, glyph: 'unwind', text: 'text-amber-600 dark:text-amber-400', badge: 'border-amber-500/40 bg-amber-500/10 text-amber-700 dark:text-amber-300' },
  rev: { icon: Binary, glyph: 'unwind', text: 'text-amber-600 dark:text-amber-400', badge: 'border-amber-500/40 bg-amber-500/10 text-amber-700 dark:text-amber-300' },
  crypto: { icon: KeyRound, glyph: 'key', text: 'text-violet-600 dark:text-violet-400', badge: 'border-violet-500/40 bg-violet-500/10 text-violet-700 dark:text-violet-300' },
  misc: { icon: Puzzle, glyph: 'puzzle', text: 'text-emerald-600 dark:text-emerald-400', badge: 'border-emerald-500/40 bg-emerald-500/10 text-emerald-700 dark:text-emerald-300' },
  penetration: { icon: ScanSearch, glyph: 'target', text: 'text-rose-600 dark:text-rose-400', badge: 'border-rose-500/40 bg-rose-500/10 text-rose-700 dark:text-rose-300' },
  pentest: { icon: ScanSearch, glyph: 'target', text: 'text-rose-600 dark:text-rose-400', badge: 'border-rose-500/40 bg-rose-500/10 text-rose-700 dark:text-rose-300' },
  forensics: { icon: ScanSearch, glyph: 'scan', text: 'text-indigo-600 dark:text-indigo-400', badge: 'border-indigo-500/40 bg-indigo-500/10 text-indigo-700 dark:text-indigo-300' },
  forensic: { icon: ScanSearch, glyph: 'scan', text: 'text-indigo-600 dark:text-indigo-400', badge: 'border-indigo-500/40 bg-indigo-500/10 text-indigo-700 dark:text-indigo-300' },
  dfir: { icon: ScanSearch, glyph: 'scan', text: 'text-indigo-600 dark:text-indigo-400', badge: 'border-indigo-500/40 bg-indigo-500/10 text-indigo-700 dark:text-indigo-300' },
  osint: { icon: Search, glyph: 'radar', text: 'text-orange-600 dark:text-orange-400', badge: 'border-orange-500/40 bg-orange-500/10 text-orange-700 dark:text-orange-300' },
  'open source intelligence': { icon: Search, glyph: 'radar', text: 'text-orange-600 dark:text-orange-400', badge: 'border-orange-500/40 bg-orange-500/10 text-orange-700 dark:text-orange-300' },
  recon: { icon: Search, glyph: 'radar', text: 'text-orange-600 dark:text-orange-400', badge: 'border-orange-500/40 bg-orange-500/10 text-orange-700 dark:text-orange-300' },
  ai: { icon: Bot, glyph: 'neural', text: 'text-lime-600 dark:text-lime-400', badge: 'border-lime-500/40 bg-lime-500/10 text-lime-700 dark:text-lime-300' },
  ml: { icon: Bot, glyph: 'neural', text: 'text-lime-600 dark:text-lime-400', badge: 'border-lime-500/40 bg-lime-500/10 text-lime-700 dark:text-lime-300' },
  'machine learning': { icon: Bot, glyph: 'neural', text: 'text-lime-600 dark:text-lime-400', badge: 'border-lime-500/40 bg-lime-500/10 text-lime-700 dark:text-lime-300' },
  llm: { icon: Bot, glyph: 'neural', text: 'text-lime-600 dark:text-lime-400', badge: 'border-lime-500/40 bg-lime-500/10 text-lime-700 dark:text-lime-300' },
  mobile: { icon: Smartphone, glyph: 'phone', text: 'text-pink-600 dark:text-pink-400', badge: 'border-pink-500/40 bg-pink-500/10 text-pink-700 dark:text-pink-300' },
  iot: { icon: Cpu, glyph: 'wireless', text: 'text-orange-600 dark:text-orange-400', badge: 'border-orange-500/40 bg-orange-500/10 text-orange-700 dark:text-orange-300' },
  hardware: { icon: CircuitBoard, glyph: 'circuit', text: 'text-orange-600 dark:text-orange-400', badge: 'border-orange-500/40 bg-orange-500/10 text-orange-700 dark:text-orange-300' },
  embedded: { icon: CircuitBoard, glyph: 'circuit', text: 'text-orange-600 dark:text-orange-400', badge: 'border-orange-500/40 bg-orange-500/10 text-orange-700 dark:text-orange-300' },
  cloud: { icon: Cloud, glyph: 'cloud', text: 'text-sky-600 dark:text-sky-400', badge: 'border-sky-500/40 bg-sky-500/10 text-sky-700 dark:text-sky-300' },
  blockchain: { icon: Link2, glyph: 'chain', text: 'text-cyan-600 dark:text-cyan-400', badge: 'border-cyan-500/40 bg-cyan-500/10 text-cyan-700 dark:text-cyan-300' },
  web3: { icon: Link2, glyph: 'chain', text: 'text-cyan-600 dark:text-cyan-400', badge: 'border-cyan-500/40 bg-cyan-500/10 text-cyan-700 dark:text-cyan-300' },
  eth: { icon: Link2, glyph: 'chain', text: 'text-cyan-600 dark:text-cyan-400', badge: 'border-cyan-500/40 bg-cyan-500/10 text-cyan-700 dark:text-cyan-300' },
}

export const challengeDirectionOptions = [
  'Misc',
  'Web',
  'Crypto',
  'Pwn',
  'Reverse',
  'Penetration',
  'Forensics',
  'OSINT',
  'AI',
  'Mobile',
  'IoT',
  'Hardware',
  'Cloud',
  'Blockchain',
] as const

export function directionStyle(direction?: string | null): DirectionStyle {
  const key = directionKey(direction)
  if (!key) return fallback
  const direct = Object.hasOwn(directionStyles, key) ? directionStyles[key] : undefined
  if (direct) return direct
  const partial = Object.entries(directionStyles).find(([name]) => key.startsWith(name))
  return partial?.[1] ?? fallback
}

/** Stable comparison key for open-text directions, independent of browser locale. */
export function directionKey(direction?: string | null): string {
  return (direction ?? '').trim().toLowerCase()
}

const directionAbbreviations: Record<string, string> = {
  ai: 'AI', osint: 'OSINT', iot: 'IoT', ml: 'ML', llm: 'LLM', dfir: 'DFIR',
}

/** Display convention: initial capital, with explicit standard abbreviation spellings. */
export function directionLabel(direction?: string | null): string {
  const key = directionKey(direction)
  return (Object.hasOwn(directionAbbreviations, key) ? directionAbbreviations[key] : undefined)
    ?? key.charAt(0).toUpperCase() + key.slice(1)
}

export function directionIcon(direction?: string | null): FunctionalComponent {
  return directionStyle(direction).icon
}

export function directionTextClass(direction?: string | null): string {
  return directionStyle(direction).text
}

export function directionBadgeClass(direction?: string | null): string {
  return directionStyle(direction).badge
}

/** Match the former badge's foreground without carrying over its surface. */
export function directionWatermarkClass(direction?: string | null): string {
  return directionStyle(direction).badge.split(' ').filter(token => token.startsWith('text-') || token.startsWith('dark:text-')).join(' ')
}

/** Shared hand-drawn glyph, resolved through the same direction aliases and fallback. */
export function directionGlyph(direction?: string | null): TechnicalIconName {
  return directionStyle(direction).glyph
}
