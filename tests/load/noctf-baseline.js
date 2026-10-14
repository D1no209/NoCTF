import http from 'k6/http'
import { check, fail, sleep } from 'k6'
import { randomBytes } from 'k6/crypto'
import execution from 'k6/execution'
import { Counter, Trend, Rate } from 'k6/metrics'

const target = parseOrigin(__ENV.BASE_URL || 'http://127.0.0.1:8080', true)
const baseUrl = target.origin
const competitionId = __ENV.COMPETITION_ID || ''
const challengeId = __ENV.COMPETITION_CHALLENGE_ID || ''
const flagValue = __ENV.FLAG_VALUE || ''
const operation = (__ENV.MUTATION_OPERATION || 'runtime-state').toLowerCase()
const writes = operation === 'flag' || operation === 'runtime-start'
const completionTimeout = positiveNumber('COMPLETION_TIMEOUT_SECONDS', 120)
const pollInterval = positiveNumber('POLL_INTERVAL_SECONDS', 1)
const requestTimeout = __ENV.REQUEST_TIMEOUT || '10s'
const requestTimeoutMs = timeoutMilliseconds(requestTimeout)
const accountInputs = readAccounts()

const operationLatency = new Trend('noctf_operation_latency', true)
const operationErrors = new Rate('noctf_operation_errors')
const rateLimited = new Counter('noctf_rate_limited')
const unavailable = new Counter('noctf_unavailable')
const rejected = new Counter('noctf_rejected')
const pendingReads = new Counter('noctf_pending_reads')
const mutationAttempts = new Counter('noctf_mutation_attempts')
const mutationAccepted = new Counter('noctf_mutation_accepted')
const mutationCompleted = new Counter('noctf_mutation_completed')
const mutationTerminal = new Counter('noctf_mutation_terminal')
const mutationFailures = new Counter('noctf_mutation_failures')
const completionErrors = new Rate('noctf_completion_errors')
const completionLatency = new Trend('noctf_completion_observation_duration', true)

validateConfiguration()

const scenarios = {
  public_reads: {
    executor: 'ramping-arrival-rate',
    exec: 'publicReads',
    startRate: arrivalRate('READ_START_ITERATIONS_PER_SECOND', 'READ_START_RPS', 5),
    timeUnit: '1s',
    preAllocatedVUs: positiveNumber('READ_PREALLOCATED_VUS', 20),
    maxVUs: positiveNumber('READ_MAX_VUS', 100),
    stages: [
      { target: arrivalRate('READ_TARGET_ITERATIONS_PER_SECOND', 'READ_TARGET_RPS', 25), duration: __ENV.RAMP_DURATION || '2m' },
      { target: arrivalRate('READ_TARGET_ITERATIONS_PER_SECOND', 'READ_TARGET_RPS', 25), duration: __ENV.STEADY_DURATION || '5m' },
      { target: 0, duration: __ENV.COOLDOWN_DURATION || '1m' },
    ],
  },
}
if (operation !== 'none') {
  scenarios.authenticated_operations = {
    executor: 'constant-arrival-rate',
    exec: 'authenticatedMutations',
    rate: arrivalRate('MUTATION_ITERATIONS_PER_SECOND', 'MUTATION_RPS', 1),
    timeUnit: '1s',
    duration: __ENV.MUTATION_DURATION || '5m',
    preAllocatedVUs: positiveNumber('MUTATION_PREALLOCATED_VUS', 5),
    maxVUs: positiveNumber('MUTATION_MAX_VUS', 20),
    startTime: __ENV.MUTATION_START || '30s',
    gracefulStop: `${Math.ceil(completionTimeout + requestTimeoutMs / 1000) + 15}s`,
    tags: { workload: operation },
  }
}

export const options = {
  discardResponseBodies: true,
  maxRedirects: 0,
  scenarios,
  // Status URLs contain unique fact/runtime IDs; keep metric series bounded.
  systemTags: ['status', 'method', 'name', 'scenario', 'expected_response', 'error_code', 'check'],
  thresholds: {
    http_req_failed: ['rate<0.02'],
    http_req_duration: ['p(95)<800', 'p(99)<2000'],
    noctf_operation_errors: ['rate<0.02'],
    noctf_operation_latency: ['p(95)<800'],
    dropped_iterations: ['count==0'],
    ...(writes ? {
      noctf_mutation_accepted: ['count>0'],
      noctf_mutation_completed: ['count>0'],
      noctf_completion_errors: ['rate==0'],
    } : {}),
  },
}

export function setup() {
  const tokens = accountInputs.map((account, index) => {
    if (account.accessToken) return account.accessToken
    const response = request('POST', '/api/v1/auth/login',
      JSON.stringify({ login: account.login, password: account.password }),
      { 'Content-Type': 'application/json' }, 'auth-login', 200, true)
    if (response.status !== 200) fail(`Account ${index + 1}: login failed with HTTP ${response.status}`)
    const body = parseJson(response)
    if (body.state !== 'Authenticated' || typeof body.accessToken !== 'string' || !body.accessToken) {
      fail(`Account ${index + 1}: authentication did not complete; finish required MFA/human verification through the supported flow`)
    }
    return body.accessToken
  })
  return { tokens }
}

export function publicReads() {
  request('GET', '/api/v1/competitions', null, {}, 'competitions-list')
  if (competitionId) {
    request('GET', `/api/v1/competitions/${competitionId}`, null, {}, 'competition-detail')
    request('GET', `/api/v1/competitions/${competitionId}/leaderboard`, null, {}, 'leaderboard')
  }
}

export function authenticatedMutations(data) {
  const token = data.tokens[execution.scenario.iterationInTest % data.tokens.length]
  if (!token) fail('No authenticated account is available')
  const headers = { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' }
  const path = `/api/v1/competitions/${competitionId}/challenges/${challengeId}`
  if (operation === 'runtime-state') {
    request('GET', `${path}/runtimes/current`, null, headers, 'runtime-state')
    return
  }

  headers['Idempotency-Key'] = uuid()
  const started = Date.now()
  mutationAttempts.add(1, { operation })
  const response = request('POST', `${path}/${operation === 'flag' ? 'flag-submissions' : 'runtimes'}`,
    operation === 'flag' ? JSON.stringify({ flag: flagValue }) : '{}', headers, operation, 202, true)
  if (response.status !== 202) return // Rejected HTTP requests are never accepted/completed business work.

  let outcome = 'invalid-accepted-response'
  try {
    const accepted = parseJson(response)
    const id = operation === 'flag' ? accepted.gameplayFactId : accepted.runtimeInstanceId
    if (!isUuid(id)) throw new Error('Accepted response has no valid operation ID')
    const statusPath = sameOriginStatusPath(accepted.statusUrl)
    mutationAccepted.add(1, { operation })
    outcome = observeCompletion(statusPath, id, headers, started + completionTimeout * 1000)
  } catch (_) {
    // Do not print response bodies, tokens, Flags, or externally supplied URLs.
    outcome = 'invalid-status-contract'
  }
  const completed = outcome === 'completed'
  completionErrors.add(!completed, { operation })
  completionLatency.add(Date.now() - started, { operation, outcome })
  if (completed) mutationCompleted.add(1, { operation })
  else mutationFailures.add(1, { operation, outcome })
  check(completed, { 'accepted operation completes successfully within the observation budget': (value) => value })
}

function observeCompletion(path, id, headers, deadline) {
  while (Date.now() < deadline) {
    const remaining = deadline - Date.now()
    const response = request('GET', path, null, headers, `${operation}-status`, 200, true,
      `${Math.max(1, Math.min(requestTimeoutMs, remaining))}ms`)
    if (Date.now() > deadline) return 'pending-timeout'
    if (response.status === 200) {
      const status = parseJson(response)
      if ((operation === 'flag' ? status.gameplayFactId : status.id) !== id) return 'operation-replaced'
      if (operation === 'flag') {
        if (status.state === 'Completed') {
          const valid = ['Correct', 'Wrong'].includes(status.result)
            && (status.timeEligibility === undefined || status.timeEligibility === 'Valid')
          const knownResults = ['Correct', 'Wrong', 'Duplicate', 'AttemptsExhausted', 'Rejected', 'RightButDue']
          mutationTerminal.add(1, { operation, result: knownResults.includes(status.result) ? status.result : 'other' })
          return valid ? 'completed' : 'business-rejected'
        }
        if (status.state === 'PlatformFailed') return 'platform-failed'
        if (!['Pending', 'Queued', 'Processing'].includes(status.state)) return 'unknown-state'
      } else {
        if (status.state === 'Running') {
          mutationTerminal.add(1, { operation, result: 'Running' })
          return 'completed'
        }
        if (['Failed', 'Stopped', 'Stopping'].includes(status.state)) return 'runtime-failed'
        if (!['Queued', 'Provisioning'].includes(status.state)) return 'unknown-state'
      }
    } else if (![0, 202, 429, 500, 502, 503, 504].includes(response.status)) {
      return 'status-http-rejected'
    }
    const remainingSleep = Math.min(pollInterval, (deadline - Date.now()) / 1000)
    if (remainingSleep > 0) sleep(remainingSleep)
  }
  return 'pending-timeout'
}

function request(method, path, body, headers, endpoint, expectedStatus = 200, readBody = false, timeout = requestTimeout) {
  const response = http.request(method, `${baseUrl}${path}`, body, {
    headers,
    tags: { endpoint, name: endpoint },
    timeout,
    redirects: 0,
    responseType: readBody ? 'text' : 'none',
    responseCallback: http.expectedStatuses(expectedStatus),
  })
  const expected = response.status === expectedStatus
  check(response, { [`${endpoint} expected HTTP status`]: () => expected })
  operationLatency.add(response.timings.duration, { endpoint })
  operationErrors.add(!expected, { endpoint })
  if (response.status === 429) rateLimited.add(1, { endpoint })
  else if (response.status === 503) unavailable.add(1, { endpoint })
  else if (response.status >= 400 && response.status < 500) rejected.add(1, { endpoint })
  if (method === 'GET' && response.status === 202) pendingReads.add(1, { endpoint })
  return response
}

function readAccounts() {
  if (operation === 'none') return []
  let accounts
  if (__ENV.NOCTF_ACCOUNTS_JSON) {
    try { accounts = JSON.parse(__ENV.NOCTF_ACCOUNTS_JSON) }
    catch (_) { throw new Error('NOCTF_ACCOUNTS_JSON must be a JSON array') }
  } else {
    accounts = [{ login: __ENV.NOCTF_LOGIN, password: __ENV.NOCTF_PASSWORD, accessToken: __ENV.NOCTF_ACCESS_TOKEN }]
  }
  if (!Array.isArray(accounts) || accounts.length === 0
    || accounts.some((account) => !account || !(typeof account.accessToken === 'string' && account.accessToken)
      && !(typeof account.login === 'string' && account.login && typeof account.password === 'string' && account.password))) {
    throw new Error('Each authenticated account requires login/password or accessToken; use MUTATION_OPERATION=none for public-only tests')
  }
  return accounts
}

function validateConfiguration() {
  const safeHosts = ['localhost', '127.0.0.1', '[::1]', 'host.docker.internal']
  if (!safeHosts.includes(target.host) && __ENV.ALLOW_REMOTE_TEST_TARGET !== 'I_UNDERSTAND') {
    throw new Error('Remote load targets require ALLOW_REMOTE_TEST_TARGET=I_UNDERSTAND and an authorized disposable environment')
  }
  if (!['none', 'runtime-state', 'runtime-start', 'flag'].includes(operation)) throw new Error('Unknown MUTATION_OPERATION')
  if (competitionId && !isUuid(competitionId)) throw new Error('COMPETITION_ID must be a UUID')
  if (operation !== 'none' && (!isUuid(competitionId) || !isUuid(challengeId))) {
    throw new Error('Authenticated operations require COMPETITION_ID and COMPETITION_CHALLENGE_ID UUIDs')
  }
  if (operation === 'flag' && !flagValue) throw new Error('FLAG_VALUE is required for Flag submissions')
}

function parseOrigin(value, originOnly = false) {
  const match = /^(https?):\/\/(\[[0-9a-f:]+\]|[a-z0-9.-]+)(?::([0-9]{1,5}))?(\/[^\s\\#]*)?$/i.exec(value)
  if (!match || originOnly && match[4] && match[4] !== '/') throw new Error('Expected an HTTP(S) origin without credentials, query or fragment')
  const protocol = match[1].toLowerCase()
  const host = match[2].toLowerCase()
  const port = match[3] ? Number(match[3]) : protocol === 'https' ? 443 : 80
  if (port < 1 || port > 65535) throw new Error('Invalid HTTP(S) port')
  const suffix = port === (protocol === 'https' ? 443 : 80) ? '' : `:${port}`
  return { origin: `${protocol}://${host}${suffix}`, host, path: match[4] || '/' }
}

function sameOriginStatusPath(value) {
  if (typeof value !== 'string' || /[\s\\#]/.test(value)) throw new Error('Invalid status URL')
  if (value.startsWith('/') && !value.startsWith('//')) return value
  const parsed = parseOrigin(value)
  if (parsed.origin !== baseUrl) throw new Error('Cross-origin status URL is forbidden')
  return parsed.path
}

function parseJson(response) {
  let body
  try { body = response.json() }
  catch (_) { throw new Error('Expected a JSON response') }
  if (!body || typeof body !== 'object') throw new Error('Expected a JSON object')
  return body
}

function timeoutMilliseconds(value) {
  const match = /^(\d+(?:\.\d+)?)(ms|s|m|h)$/.exec(value)
  if (!match || Number(match[1]) <= 0) throw new Error('REQUEST_TIMEOUT must be positive with an ms, s, m or h suffix')
  return Number(match[1]) * { ms: 1, s: 1000, m: 60000, h: 3600000 }[match[2]]
}

function positiveNumber(name, fallback) {
  const value = Number(__ENV[name] || fallback)
  if (!Number.isFinite(value) || value <= 0) throw new Error(`${name} must be positive`)
  return value
}

function arrivalRate(name, legacyName, fallback) {
  const value = Number(__ENV[name] || __ENV[legacyName] || fallback)
  if (!Number.isInteger(value) || value < 0) throw new Error(`${name} must be a nonnegative integer`)
  return value
}

function isUuid(value) {
  return typeof value === 'string' && value !== '00000000-0000-0000-0000-000000000000'
    && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value)
}

function uuid() {
  const bytes = new Uint8Array(randomBytes(16))
  bytes[6] = (bytes[6] & 0x0f) | 0x40
  bytes[8] = (bytes[8] & 0x3f) | 0x80
  const hex = Array.from(bytes, (value) => value.toString(16).padStart(2, '0')).join('')
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`
}
