/** Fictional bounded history for local log pagination verification. */
export function createMockPlatformLogHistory() {
  const seedTime = Date.now()
  const logs = Array.from({ length: 125 }, (_, index) => ({
    cursor: `mock-log-${index}`, timestamp: new Date(seedTime - (index + 1) * 60_000).toISOString(),
    level: 'Warning', service: index % 2 ? 'Worker' : 'Runner', category: 'NoCTF.Mock.Pagination',
    message: `Local pagination sample ${125 - index}`, exceptionType: null, exceptionMessage: null,
  }))
  return (url: URL) => {
    const from = url.searchParams.get('from'), to = url.searchParams.get('to')
    const service = url.searchParams.get('service'), search = url.searchParams.get('search')?.toLowerCase()
    const minimum = url.searchParams.get('minimumLevel')
    const rows = logs.filter(log => (!from || Date.parse(log.timestamp) >= Date.parse(from))
      && (!to || Date.parse(log.timestamp) <= Date.parse(to))
      && (!service || log.service === service)
      && (!search || `${log.message} ${log.category}`.toLowerCase().includes(search))
      && !['Error', 'Critical'].includes(minimum ?? ''))
    const cursor = url.searchParams.get('cursor')
    const offset = cursor ? rows.findIndex(row => row.cursor === cursor) + 1 : 0
    const limit = Math.min(200, Math.max(1, Number(url.searchParams.get('limit') ?? 50)))
    const items = rows.slice(offset, offset + limit)
    return { items, nextCursor: offset + items.length < rows.length ? items.at(-1)?.cursor : null }
  }
}
