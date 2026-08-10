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
  score?: number
  solveCount?: number
  lastScoreAt?: string | null
  cells?: LeaderboardCell[]
}

/** 归一化 GUID 文本，用于题目与稀疏 Cell 的 Map 比较。 */
export function normalizeChallengeKey(value?: string | null): string {
  return (value ?? '').replace(/[^0-9a-f]/gi, '').toLowerCase()
}

export const bloodRankLabel: Record<string, string> = { First: translate("一血"), Second: translate("二血"), Third: translate("三血") }
