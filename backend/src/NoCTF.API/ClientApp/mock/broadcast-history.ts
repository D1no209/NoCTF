import { id, type Data } from './schema'

/** Fictional bounded history for local broadcast scroll verification. */
export function createMockBroadcastHistory(state: { competitions: Data[]; challenges: Data[] }) {
  const seedTime = Date.now()
  const events = state.competitions.flatMap((competition, competitionIndex) => Array.from({ length: 80 }, (_, index) => ({
    id: id(99001, competitionIndex * 100 + index + 1),
    competitionId: competition.id, kind: 'ChallengePublished', level: 'Information',
    competitionChallengeId: state.challenges.find(challenge => challenge.competitionId === competition.id)?.id,
    challengeTitle: `本地演示题目 / Demo challenge ${80 - index}`,
    occurredAt: new Date(seedTime - (index + 1) * 60_000).toISOString(),
  })))
  return (competitionId: string, url: URL) => {
    const from = url.searchParams.get('from'), to = url.searchParams.get('to')
    const kind = url.searchParams.get('kind')
    const rows = events.filter(event => event.competitionId === competitionId
      && (!from || Date.parse(event.occurredAt) >= Date.parse(from))
      && (!to || Date.parse(event.occurredAt) <= Date.parse(to))
      && (!kind || kind === event.kind))
    const cursor = url.searchParams.get('cursor')
    const offset = cursor ? rows.findIndex(row => row.id === cursor) + 1 : Number(url.searchParams.get('offset') ?? 0)
    const limit = Math.min(200, Math.max(1, Number(url.searchParams.get('limit') ?? 20)))
    const items = rows.slice(offset, offset + limit)
    return { items, total: rows.length, nextCursor: offset + items.length < rows.length ? items.at(-1)?.id : null }
  }
}
