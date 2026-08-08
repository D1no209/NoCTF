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

export interface PenaltyRecordItem {
  at?: string
  points?: number
  kind?: string
}

export interface TrendSeries {
  teamId?: string
  teamName?: string
  points?: TrendPoint[]
  solves?: SolveRecordItem[]
  penalties?: PenaltyRecordItem[]
}

export interface ChallengeInfo {
  competitionChallengeId?: string
  title?: string
  direction?: string
}

/** slotKey 形如 `challenge:<guidN>`/`service:<guid>`,归一化成可比较的题目 id。 */
export function normalizeChallengeKey(value?: string | null): string {
  return (value ?? '').replace(/[^0-9a-f]/gi, '').toLowerCase()
}

export function penaltyKindLabel(kind?: string): string {
  return ({ WrongSubmission: '错误提交扣分', HintUnlock: '解锁提示扣分' } as Record<string, string>)[String(kind)] ?? '扣分'
}

export const bloodRankLabel: Record<string, string> = { First: '一血', Second: '二血', Third: '三血' }
