import { afterAll, afterEach, beforeEach, describe, expect, test } from 'bun:test'
import { client } from '../src/api/generated/client.gen'
import { authApi, platformAdminApi } from '../src/api/noctf'

const apiBaseUrl = 'https://api.noctf.test'
const originalClientConfig = client.getConfig()
const originalFetch = globalThis.fetch
const requests: Request[] = []

const configuration = {
  enabled: false,
  publicBaseUrl: 'https://noctf.example',
  tokenLifetimeMinutes: 1440,
  resendCooldownSeconds: 60,
  passwordResetTokenLifetimeMinutes: 30,
  passwordResetCooldownSeconds: 60,
  passwordResetMaxRequestsPerHour: 3,
  smtpHost: 'smtp.example',
  smtpPort: 587,
  smtpSecurityMode: 'StartTls' as const,
  smtpUserName: 'mailer',
  smtpPasswordConfigured: true,
  smtpFromAddress: 'no-reply@noctf.example',
  smtpFromName: 'NoCTF',
  smtpTimeoutSeconds: 10,
  revision: 4,
  updatedAt: '2026-08-01T00:00:00Z',
}

const contractFetch: typeof fetch = async (input, init) => {
  const normalizedInput = typeof input === 'string' && input.startsWith('/')
    ? new URL(input, apiBaseUrl)
    : input
  const request = new Request(normalizedInput, init)
  requests.push(request)
  const path = new URL(request.url).pathname

  if (path === '/api/v1/admin/platform/email-verification/test'
    || path === '/api/v1/auth/email-verification/resend'
    || path === '/api/v1/auth/email-verification/verify'
    || path === '/api/v1/auth/password-reset/complete') {
    return new Response(null, { status: 204 })
  }
  if (path === '/api/v1/auth/password-reset/request')
    return new Response(null, { status: 202 })

  return Response.json(configuration)
}

beforeEach(() => {
  requests.length = 0
  globalThis.fetch = contractFetch
  client.setConfig({
    baseUrl: apiBaseUrl,
    fetch: contractFetch,
    headers: { Authorization: 'Bearer admin-token' },
  })
})

afterEach(() => {
  client.setConfig({ headers: { Authorization: null } })
})

afterAll(() => {
  globalThis.fetch = originalFetch
  client.setConfig(originalClientConfig)
})

describe('generated email verification contracts', () => {
  test('admin settings keep the SMTP password in a separate write-only operation', async () => {
    const loaded = await platformAdminApi.emailVerificationConfiguration()
    await platformAdminApi.updateEmailVerificationConfiguration({
      enabled: true,
      publicBaseUrl: configuration.publicBaseUrl,
      tokenLifetimeMinutes: configuration.tokenLifetimeMinutes,
      resendCooldownSeconds: configuration.resendCooldownSeconds,
      passwordResetTokenLifetimeMinutes: configuration.passwordResetTokenLifetimeMinutes,
      passwordResetCooldownSeconds: configuration.passwordResetCooldownSeconds,
      passwordResetMaxRequestsPerHour: configuration.passwordResetMaxRequestsPerHour,
      smtpHost: configuration.smtpHost,
      smtpPort: configuration.smtpPort,
      smtpSecurityMode: configuration.smtpSecurityMode,
      smtpUserName: configuration.smtpUserName,
      smtpFromAddress: configuration.smtpFromAddress,
      smtpFromName: configuration.smtpFromName,
      smtpTimeoutSeconds: configuration.smtpTimeoutSeconds,
      expectedRevision: configuration.revision,
    })
    await platformAdminApi.replaceEmailVerificationPassword('replacement-secret', 4)
    await platformAdminApi.sendEmailVerificationTest()

    expect('smtpPassword' in loaded).toBe(false)
    expect(requests.map(request => `${request.method} ${new URL(request.url).pathname}`)).toEqual([
      'GET /api/v1/admin/platform/email-verification/configuration',
      'PUT /api/v1/admin/platform/email-verification/configuration',
      'PUT /api/v1/admin/platform/email-verification/password',
      'POST /api/v1/admin/platform/email-verification/test',
    ])
    expect(await requests[2]!.json()).toEqual({
      password: 'replacement-secret',
      expectedRevision: 4,
    })
    expect(await requests[1]!.json()).toEqual({
      enabled: true,
      publicBaseUrl: configuration.publicBaseUrl,
      tokenLifetimeMinutes: 1440,
      resendCooldownSeconds: 60,
      passwordResetTokenLifetimeMinutes: 30,
      passwordResetCooldownSeconds: 60,
      passwordResetMaxRequestsPerHour: 3,
      smtpHost: configuration.smtpHost,
      smtpPort: 587,
      smtpSecurityMode: 'StartTls',
      smtpUserName: configuration.smtpUserName,
      smtpFromAddress: configuration.smtpFromAddress,
      smtpFromName: configuration.smtpFromName,
      smtpTimeoutSeconds: 10,
      expectedRevision: 4,
    })
  })

  test('verification and authenticated resend use the generated v1 endpoints', async () => {
    await authApi.verifyEmail('single-use-token')
    await authApi.resendEmailVerification()

    expect(requests.map(request => new URL(request.url).pathname)).toEqual([
      '/api/v1/auth/email-verification/verify',
      '/api/v1/auth/email-verification/resend',
    ])
    expect(await requests[0]!.json()).toEqual({ token: 'single-use-token' })
    expect(requests[1]!.headers.get('Authorization')).toBe('Bearer admin-token')
  })

  test('password recovery uses generated anonymous endpoints without exposing account state', async () => {
    client.setConfig({ headers: { Authorization: null } })

    await authApi.requestPasswordReset('player@example.test')
    await authApi.completePasswordReset('single-use-reset-token', 'eight888')

    expect(requests.map(request => `${request.method} ${new URL(request.url).pathname}`)).toEqual([
      'POST /api/v1/auth/password-reset/request',
      'POST /api/v1/auth/password-reset/complete',
    ])
    expect(await requests[0]!.json()).toEqual({ email: 'player@example.test' })
    expect(await requests[1]!.json()).toEqual({
      token: 'single-use-reset-token',
      newPassword: 'eight888',
    })
    expect(requests.every(request => request.headers.get('Authorization') === null)).toBe(true)
  })
})
