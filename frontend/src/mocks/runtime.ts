interface MockRoute {
  method: string
  path: string
  status?: number
  headers?: Record<string, string>
  query?: Record<string, string | number | boolean>
  body?: unknown
}

interface MockConfig {
  routes?: MockRoute[]
}

const MOCK_HEADER = 'x-noctf-mock'
let mockConfigPromise: Promise<MockConfig> | null = null

export function areMocksEnabled(isDevelopment: boolean, configuredValue?: string) {
  return isDevelopment && configuredValue === 'true'
}

function mocksEnabled() {
  return areMocksEnabled(import.meta.env.DEV, import.meta.env.VITE_ENABLE_MOCKS)
}

function emptyConfig(): MockConfig {
  return { routes: [] }
}

async function loadMockConfig(baseFetch: typeof fetch): Promise<MockConfig> {
  if (!mocksEnabled())
    return emptyConfig()

  if (!mockConfigPromise) {
    mockConfigPromise = baseFetch('/mock-data.json', {
      cache: 'no-store',
      headers: { Accept: 'application/json' },
    })
      .then(async (response) => {
        if (!response.ok)
          return emptyConfig()
        const data = await response.json().catch(() => null)
        if (!data || typeof data !== 'object' || !Array.isArray((data as MockConfig).routes))
          return emptyConfig()
        return data as MockConfig
      })
      .catch(() => emptyConfig())
  }

  return mockConfigPromise
}

function normalizeUrl(input: RequestInfo | URL): URL {
  if (input instanceof URL)
    return input
  if (typeof input === 'string')
    return new URL(input, globalThis.location.origin)
  return new URL(input.url, globalThis.location.origin)
}

function getMethod(input: RequestInfo | URL, init?: RequestInit) {
  if (init?.method)
    return init.method.toUpperCase()
  if (input instanceof Request)
    return input.method.toUpperCase()
  return 'GET'
}

function matchesQuery(url: URL, query?: MockRoute['query']) {
  if (!query)
    return true

  return Object.entries(query).every(([key, value]) => url.searchParams.get(key) === String(value))
}

function matchesPath(actualPath: string, expectedPath: string) {
  const actualSegments = actualPath.split('/').filter(Boolean)
  const expectedSegments = expectedPath.split('/').filter(Boolean)

  if (actualSegments.length !== expectedSegments.length)
    return false

  return expectedSegments.every((segment, index) => segment.startsWith(':') || segment === actualSegments[index])
}

function findRoute(routes: MockRoute[], method: string, url: URL) {
  return routes.find(route =>
    route.method.toUpperCase() === method
    && matchesPath(url.pathname, route.path)
    && matchesQuery(url, route.query),
  )
}

function isApiRequest(url: URL) {
  return url.pathname.startsWith('/api/')
}

function extractCompetitionId(pathname: string): string | null {
  const segments = pathname.split('/').filter(Boolean)
  if (segments.length >= 3 && segments[0] === 'api' && segments[1] === 'competitions')
    return segments[2] ?? null
  return null
}

async function resolveMockBody(body: unknown, competitionId: string | null): Promise<unknown> {
  if (!body || typeof body !== 'object' || !('__mockGenerate' in (body as Record<string, unknown>)))
    return body

  const generator = (body as Record<string, unknown>).__mockGenerate
  if (!mocksEnabled() || typeof generator !== 'string')
    return null

  const id = competitionId ?? 'mock-competition'

  if (generator === 'leaderboard') {
    const { generateLeaderboardEntries } = await import(/* @vite-ignore */ '@/mocks/leaderboardMock')
    return generateLeaderboardEntries(id, 100)
  }

  if (generator === 'leaderboardTrend') {
    const { generateLeaderboardTrend } = await import(/* @vite-ignore */ '@/mocks/leaderboardMock')
    return generateLeaderboardTrend(id)
  }

  return null
}

export function createMockAwareFetch(baseFetch: typeof fetch): typeof fetch {
  return async (input, init) => {
    if (!mocksEnabled())
      return baseFetch(input, init)

    const url = normalizeUrl(input)
    if (!isApiRequest(url))
      return baseFetch(input, init)

    const config = await loadMockConfig(baseFetch)
    const route = findRoute(config.routes ?? [], getMethod(input, init), url)
    if (!route)
      return baseFetch(input, init)

    const body = await resolveMockBody(route.body, extractCompetitionId(url.pathname))

    return new Response(JSON.stringify(body ?? null), {
      status: route.status ?? 200,
      headers: {
        'content-type': 'application/json',
        [MOCK_HEADER]: '1',
        ...route.headers,
      },
    })
  }
}
