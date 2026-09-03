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

interface DirectionStyle {
  icon: FunctionalComponent
  /** 图标/标题等着色 */
  text: string
  /** 方向徽章配色(描边 + 浅底 + 着色文字) */
  badge: string
}

const fallback: DirectionStyle = {
  icon: Flag,
  text: 'text-muted-foreground',
  badge: 'border-border bg-muted/50 text-muted-foreground',
}

/**
 * 题目方向 → 图标 + 颜色的唯一映射表。
 * direction 是开放文本,按小写前缀/包含关系匹配,未知名称走 fallback。
 */
const directionStyles: Record<string, DirectionStyle> = {
  web: { icon: Globe, text: 'text-blue-600 dark:text-blue-400', badge: 'border-blue-500/40 bg-blue-500/10 text-blue-700 dark:text-blue-300' },
  pwn: { icon: Bug, text: 'text-red-600 dark:text-red-400', badge: 'border-red-500/40 bg-red-500/10 text-red-700 dark:text-red-300' },
  reverse: { icon: Binary, text: 'text-amber-600 dark:text-amber-400', badge: 'border-amber-500/40 bg-amber-500/10 text-amber-700 dark:text-amber-300' },
  rev: { icon: Binary, text: 'text-amber-600 dark:text-amber-400', badge: 'border-amber-500/40 bg-amber-500/10 text-amber-700 dark:text-amber-300' },
  crypto: { icon: KeyRound, text: 'text-violet-600 dark:text-violet-400', badge: 'border-violet-500/40 bg-violet-500/10 text-violet-700 dark:text-violet-300' },
  misc: { icon: Puzzle, text: 'text-emerald-600 dark:text-emerald-400', badge: 'border-emerald-500/40 bg-emerald-500/10 text-emerald-700 dark:text-emerald-300' },
  forensics: { icon: ScanSearch, text: 'text-indigo-600 dark:text-indigo-400', badge: 'border-indigo-500/40 bg-indigo-500/10 text-indigo-700 dark:text-indigo-300' },
  forensic: { icon: ScanSearch, text: 'text-indigo-600 dark:text-indigo-400', badge: 'border-indigo-500/40 bg-indigo-500/10 text-indigo-700 dark:text-indigo-300' },
  dfir: { icon: ScanSearch, text: 'text-indigo-600 dark:text-indigo-400', badge: 'border-indigo-500/40 bg-indigo-500/10 text-indigo-700 dark:text-indigo-300' },
  osint: { icon: Search, text: 'text-orange-600 dark:text-orange-400', badge: 'border-orange-500/40 bg-orange-500/10 text-orange-700 dark:text-orange-300' },
  'open source intelligence': { icon: Search, text: 'text-orange-600 dark:text-orange-400', badge: 'border-orange-500/40 bg-orange-500/10 text-orange-700 dark:text-orange-300' },
  recon: { icon: Search, text: 'text-orange-600 dark:text-orange-400', badge: 'border-orange-500/40 bg-orange-500/10 text-orange-700 dark:text-orange-300' },
  ai: { icon: Bot, text: 'text-lime-600 dark:text-lime-400', badge: 'border-lime-500/40 bg-lime-500/10 text-lime-700 dark:text-lime-300' },
  ml: { icon: Bot, text: 'text-lime-600 dark:text-lime-400', badge: 'border-lime-500/40 bg-lime-500/10 text-lime-700 dark:text-lime-300' },
  'machine learning': { icon: Bot, text: 'text-lime-600 dark:text-lime-400', badge: 'border-lime-500/40 bg-lime-500/10 text-lime-700 dark:text-lime-300' },
  llm: { icon: Bot, text: 'text-lime-600 dark:text-lime-400', badge: 'border-lime-500/40 bg-lime-500/10 text-lime-700 dark:text-lime-300' },
  mobile: { icon: Smartphone, text: 'text-pink-600 dark:text-pink-400', badge: 'border-pink-500/40 bg-pink-500/10 text-pink-700 dark:text-pink-300' },
  iot: { icon: Cpu, text: 'text-orange-600 dark:text-orange-400', badge: 'border-orange-500/40 bg-orange-500/10 text-orange-700 dark:text-orange-300' },
  hardware: { icon: CircuitBoard, text: 'text-orange-600 dark:text-orange-400', badge: 'border-orange-500/40 bg-orange-500/10 text-orange-700 dark:text-orange-300' },
  embedded: { icon: CircuitBoard, text: 'text-orange-600 dark:text-orange-400', badge: 'border-orange-500/40 bg-orange-500/10 text-orange-700 dark:text-orange-300' },
  cloud: { icon: Cloud, text: 'text-sky-600 dark:text-sky-400', badge: 'border-sky-500/40 bg-sky-500/10 text-sky-700 dark:text-sky-300' },
  blockchain: { icon: Link2, text: 'text-cyan-600 dark:text-cyan-400', badge: 'border-cyan-500/40 bg-cyan-500/10 text-cyan-700 dark:text-cyan-300' },
  web3: { icon: Link2, text: 'text-cyan-600 dark:text-cyan-400', badge: 'border-cyan-500/40 bg-cyan-500/10 text-cyan-700 dark:text-cyan-300' },
  eth: { icon: Link2, text: 'text-cyan-600 dark:text-cyan-400', badge: 'border-cyan-500/40 bg-cyan-500/10 text-cyan-700 dark:text-cyan-300' },
}

export function directionStyle(direction?: string | null): DirectionStyle {
  const key = (direction ?? '').trim().toLowerCase()
  if (!key) return fallback
  const direct = directionStyles[key]
  if (direct) return direct
  const partial = Object.entries(directionStyles).find(([name]) => key.startsWith(name))
  return partial?.[1] ?? fallback
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
