import { translate } from '../../utils/i18n'

/** 排行榜页面内部使用的结构化类型(对应后端 LeaderboardProtocolResponse 的 camelCase JSON)。 */

export interface TrendPoint {
  at?: Date | string | null
  score?: number | null
}

export interface SolveRecordItem {
  competitionChallengeId?: string | null
  at?: Date | string | null
  points?: number | null
  solveOrdinal?: number | null
  submitterName?: string | null
}

export interface TrendSeries {
  teamId?: string | null
  teamName?: string | null
  points?: TrendPoint[]
  solves?: SolveRecordItem[]
}

export interface ChallengeInfo {
  competitionChallengeId?: string | null
  title?: string | null
  direction?: string | null
}

export interface LeaderboardCell {
  competitionChallengeId?: string | null
  score?: number | null
  attackScore?: number | null
  defenseScore?: number | null
  solvedAt?: string | null
  solverName?: string | null
  bloodRank?: string | null
}

export interface MatrixEntry {
  rank?: number | null
  teamId?: string | null
  teamName?: string | null
  trackKey?: string | null
  score?: number | null
  attackScore?: number | null
  defenseScore?: number | null
  penaltyScore?: number | null
  solveCount?: number | null
  lastScoreAt?: string | null
  cells?: LeaderboardCell[]
}

/** 归一化 GUID 文本，用于题目与稀疏 Cell 的 Map 比较。 */
export function normalizeChallengeKey(value?: string | null): string {
  return (value ?? '').replace(/[^0-9a-f]/gi, '').toLowerCase()
}

const bloodRankLabelKey: Record<string, string> = { First: "common.label.firstBlood", Second: "common.label.secondBlood", Third: "common.label.thirdBlood" }

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
export function medalBloodRankClass(bloodRank?: string | null): string | null | undefined {
  const normalized = bloodRank?.replace(/Blood$/, '')
  const index = normalized
    ? bloodRankOrder.indexOf(normalized as (typeof bloodRankOrder)[number])
    : -1
  return index >= 0 ? medalRankClass[index + 1] : undefined
}
