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

type MockMode = 'on' | 'off' | 'auto'

const MOCK_HEADER = 'x-noctf-mock'
let mockConfigPromise: Promise<MockConfig> | null = null
let autoMockActive = false
let backendProbePromise: Promise<boolean> | null = null
const reportedMisses = new Set<string>()

export function areMocksEnabled(isDevelopment: boolean, configuredValue?: string) {
  return isDevelopment && configuredValue === 'true'
}

/**
 * Mock serving mode:
 * - 'on'   — VITE_ENABLE_MOCKS=true; every registered route is served from mock data.
 * - 'off'  — production builds, or VITE_ENABLE_MOCKS=false.
 * - 'auto' — development default; requests pass through to the real backend until it
 *            proves unreachable, then the session transparently switches to mock data.
 */
function mockMode(): MockMode {
  if (!import.meta.env.DEV)
    return 'off'
  const configured = import.meta.env.VITE_ENABLE_MOCKS
  if (configured === 'true')
    return 'on'
  if (configured === 'false')
    return 'off'
  return 'auto'
}

function mocksEnabled() {
  const mode = mockMode()
  return mode === 'on' || (mode === 'auto' && autoMockActive)
}

/** True once the dev mock runtime is actually serving responses. */
export function isMockRuntimeActive() {
  return mocksEnabled()
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

function isAbortError(error: unknown) {
  return error instanceof DOMException && error.name === 'AbortError'
}

/**
 * The vite dev proxy answers with a 5xx when the backend target is down, so a
 * request can fail either by throwing (direct connection) or by returning 5xx
 * (proxied). A health probe disambiguates "backend down" from a genuine
 * application-level error on a live backend.
 */
function probeBackendAlive(baseFetch: typeof fetch, url: URL) {
  if (!backendProbePromise) {
    const healthUrl = new URL('/api/health', url.origin).toString()
    backendProbePromise = baseFetch(healthUrl, {
      cache: 'no-store',
      headers: { Accept: 'application/json' },
    })
      .then(response => response.status < 500)
      .catch(() => false)
      .finally(() => {
        backendProbePromise = null
      })
  }
  return backendProbePromise
}

function activateAutoMock() {
  if (autoMockActive)
    return
  autoMockActive = true
  console.info(
    '[noctf-mock] Backend unreachable — this dev session now serves mock data. '
    + 'Start the backend and reload to leave mock mode, or set VITE_ENABLE_MOCKS=false to opt out.',
  )
}

function mockMissResponse(method: string, url: URL) {
  const key = `${method} ${url.pathname}`
  if (!reportedMisses.has(key)) {
    reportedMisses.add(key)
    console.warn(`[noctf-mock] No mock route registered for ${key}. Add it to src/mocks/mock-data.json.`)
  }
  return new Response(JSON.stringify({ message: `Mock route missing: ${key}` }), {
    status: 404,
    headers: { 'content-type': 'application/json', [MOCK_HEADER]: 'miss' },
  })
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
    const mode = mockMode()
    if (mode === 'off')
      return baseFetch(input, init)

    const url = normalizeUrl(input)
    if (!isApiRequest(url))
      return baseFetch(input, init)

    if (mode === 'auto' && !autoMockActive) {
      try {
        const response = await baseFetch(input, init)
        if (response.status < 500 || await probeBackendAlive(baseFetch, url))
          return response
        activateAutoMock()
      }
      catch (error) {
        if (isAbortError(error) || await probeBackendAlive(baseFetch, url))
          throw error
        activateAutoMock()
      }
    }

    const config = await loadMockConfig(baseFetch)
    const route = findRoute(config.routes ?? [], getMethod(input, init), url)
    if (!route) {
      // Forced mock mode is allowed to mix with a live backend; auto mode only
      // activates once the backend is gone, so a miss is answered with a
      // diagnosable 404 instead of a second doomed request.
      if (mode === 'auto')
        return mockMissResponse(getMethod(input, init), url)
      return baseFetch(input, init)
    }

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
