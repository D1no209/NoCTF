import { translate } from '../../utils/i18n'

/** 排行榜页面内部使用的结构化类型(对应后端 LeaderboardProtocolResponse 的 camelCase JSON)。 */

export interface TrendPoint {
  at?: string
  score?: number
}

export interface SolveRecordItem {
  competitionChallengeId?: string
  at?: string
  points?: number
  solveOrdinal?: number
  submitterName?: string | null
}

export interface TrendSeries {
  teamId?: string
  teamName?: string
  points?: TrendPoint[]
  solves?: SolveRecordItem[]
}

export interface ChallengeInfo {
  competitionChallengeId?: string
  title?: string
  direction?: string
}

export interface LeaderboardCell {
  competitionChallengeId?: string
  score?: number
  solvedAt?: string | null
  solverName?: string | null
  bloodRank?: string | null
}

export interface MatrixEntry {
  rank?: number
  teamId?: string
  teamName?: string
  trackKey?: string
  score?: number
  solveCount?: number
  lastScoreAt?: string | null
  cells?: LeaderboardCell[]
}

/** 归一化 GUID 文本，用于题目与稀疏 Cell 的 Map 比较。 */
export function normalizeChallengeKey(value?: string | null): string {
  return (value ?? '').replace(/[^0-9a-f]/gi, '').toLowerCase()
}

const bloodRankLabelKey: Record<string, string> = { First: '一血', Second: '二血', Third: '三血' }

export function bloodRankLabel(rank?: string | null): string {
  const key = rank ? bloodRankLabelKey[rank] : undefined
  return key ? translate(key) : ''
}

const bloodRankOrder = ['First', 'Second', 'Third'] as const

/** 金/银/铜 Medal 图标着色,总榜名次(rank 1-3)与题目血榜(First/Second/Third)共用。 */
export const medalRankClass: Record<number, string> = {
  1: 'text-amber-500',
  2: 'text-slate-400',
  3: 'text-orange-600',
}

/** 血榜名次 → Medal 图标着色;非前三名返回 undefined(调用方自行兜底)。 */
export function medalBloodRankClass(bloodRank?: string | null): string | undefined {
  const index = bloodRank
    ? bloodRankOrder.indexOf(bloodRank as (typeof bloodRankOrder)[number])
    : -1
  return index >= 0 ? medalRankClass[index + 1] : undefined
}
