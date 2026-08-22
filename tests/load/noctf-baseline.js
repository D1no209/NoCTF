import http from 'k6/http'
import { check, fail, sleep } from 'k6'
import { Trend, Rate } from 'k6/metrics'

const baseUrl = (__ENV.BASE_URL || 'http://127.0.0.1:8080').replace(/\/$/, '')
const competitionId = __ENV.COMPETITION_ID || ''
const challengeId = __ENV.COMPETITION_CHALLENGE_ID || ''
const login = __ENV.NOCTF_LOGIN || ''
const password = __ENV.NOCTF_PASSWORD || ''
const flagValue = __ENV.FLAG_VALUE || ''

const operationLatency = new Trend('noctf_operation_latency', true)
const operationErrors = new Rate('noctf_operation_errors')

guardTarget()

export const options = {
  discardResponseBodies: true,
  scenarios: {
    public_reads: {
      executor: 'ramping-arrival-rate',
      exec: 'publicReads',
      startRate: Number(__ENV.READ_START_RPS || 5),
      timeUnit: '1s',
      preAllocatedVUs: Number(__ENV.READ_PREALLOCATED_VUS || 20),
      maxVUs: Number(__ENV.READ_MAX_VUS || 100),
      stages: [
        { target: Number(__ENV.READ_TARGET_RPS || 25), duration: __ENV.RAMP_DURATION || '2m' },
        { target: Number(__ENV.READ_TARGET_RPS || 25), duration: __ENV.STEADY_DURATION || '5m' },
        { target: 0, duration: __ENV.COOLDOWN_DURATION || '1m' },
      ],
    },
    authenticated_mutations: {
      executor: 'constant-arrival-rate',
      exec: 'authenticatedMutations',
      rate: Number(__ENV.MUTATION_RPS || 1),
      timeUnit: '1s',
      duration: __ENV.MUTATION_DURATION || '5m',
      preAllocatedVUs: Number(__ENV.MUTATION_PREALLOCATED_VUS || 5),
      maxVUs: Number(__ENV.MUTATION_MAX_VUS || 20),
      startTime: __ENV.MUTATION_START || '30s',
      tags: { workload: 'mutation' },
    },
  },
  thresholds: {
    http_req_failed: ['rate<0.02'],
    http_req_duration: ['p(95)<800', 'p(99)<2000'],
    noctf_operation_errors: ['rate<0.02'],
    noctf_operation_latency: ['p(95)<800'],
  },
}

export function setup() {
  if (!login || !password) return { token: '' }

  const response = http.post(
    `${baseUrl}/api/v1/auth/login`,
    JSON.stringify({ login, password }),
    {
      headers: { 'Content-Type': 'application/json' },
      tags: { endpoint: 'auth-login' },
      responseType: 'text',
    },
  )
  if (!check(response, { 'login succeeds': (value) => value.status === 200 })) {
    fail(`Authentication failed with HTTP ${response.status}`)
  }
  return { token: response.json('accessToken') }
}

export function publicReads() {
  request('GET', '/api/v1/competitions', null, {}, 'competitions-list', [200])
  if (competitionId) {
    request('GET', `/api/v1/competitions/${competitionId}`, null, {}, 'competition-detail', [200])
    request('GET', `/api/v1/competitions/${competitionId}/leaderboard`, null, {}, 'leaderboard', [200, 202, 404])
    request('GET', `/api/v1/competitions/${competitionId}/events`, null, {}, 'competition-events', [200, 403, 404])
  }
  sleep(Number(__ENV.READ_THINK_SECONDS || 0.2))
}

export function authenticatedMutations(data) {
  if (!data.token || !competitionId || !challengeId) return

  const headers = {
    Authorization: `Bearer ${data.token}`,
    'Content-Type': 'application/json',
  }
  const operation = (__ENV.MUTATION_OPERATION || 'runtime-state').toLowerCase()
  switch (operation) {
    case 'flag':
      if (!flagValue) return
      request(
        'POST',
        `/api/v1/competitions/${competitionId}/challenges/${challengeId}/flag-submissions`,
        JSON.stringify({ flag: flagValue }),
        headers,
        'flag-submit',
        [202, 409, 422, 429],
      )
      break
    case 'runtime-start':
      request(
        'POST',
        `/api/v1/competitions/${competitionId}/challenges/${challengeId}/runtime/start`,
        null,
        headers,
        'runtime-start',
        [202, 404, 409, 429, 503],
      )
      break
    case 'runtime-state':
    default:
      request(
        'GET',
        `/api/v1/competitions/${competitionId}/challenges/${challengeId}/runtime`,
        null,
        headers,
        'runtime-state',
        [200, 404],
      )
      break
  }
  sleep(Number(__ENV.MUTATION_THINK_SECONDS || 0.5))
}

function request(method, path, body, headers, endpoint, acceptedStatuses) {
  const response = http.request(method, `${baseUrl}${path}`, body, {
    headers,
    tags: { endpoint },
    timeout: __ENV.REQUEST_TIMEOUT || '10s',
  })
  const accepted = acceptedStatuses.includes(response.status)
  check(response, { [`${endpoint} accepted status`]: () => accepted })
  operationLatency.add(response.timings.duration, { endpoint })
  operationErrors.add(!accepted, { endpoint })
  return response
}

function guardTarget() {
  const match = /^(?:https?):\/\/(\[[^\]]+\]|[^/:?#]+)(?::\d+)?(?:[/?#]|$)/i.exec(baseUrl)
  if (!match) {
    throw new Error(`BASE_URL is invalid: ${baseUrl}`)
  }
  const hostname = match[1].replace(/^\[|\]$/g, '').toLowerCase()

  const safeHosts = new Set(['localhost', '127.0.0.1', '::1', 'host.docker.internal'])
  if (!safeHosts.has(hostname) && __ENV.ALLOW_REMOTE_TEST_TARGET !== 'I_UNDERSTAND') {
    throw new Error(
      'Remote load targets are disabled. Set ALLOW_REMOTE_TEST_TARGET=I_UNDERSTAND only for an authorized disposable environment.',
    )
  }
}
