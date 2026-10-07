import { describe, expect, test } from 'bun:test'
import { isEnrollmentFlow, normalizeMfaCode, validMfaCode, safeMfaReturnPath } from '../app/features/authentication/mfa/flow'
import { requiresInteractiveAuthentication, shouldRefreshSession } from '../app/lib/auth-refresh'
import { ensureLocaleDomains } from '../app/utils/i18n'
import { parseApiError } from '../app/utils/api-error'
import { createMockApi } from '../mock/api'

describe('MFA input and session handling', () => {
  test('accepts pasted TOTP and grouped recovery codes without accepting malformed input', () => {
    expect(normalizeMfaCode('123 456', 'Totp')).toBe('123456')
    expect(validMfaCode('123 456', 'Totp')).toBeTrue()
    expect(validMfaCode('12345', 'Totp')).toBeFalse()
    expect(validMfaCode('１２３４５６', 'Totp')).toBeFalse()
    expect(validMfaCode('abcdef12-34567890-abcdef12-34567890', 'RecoveryCode')).toBeTrue()
    expect(validMfaCode('123456', 'RecoveryCode')).toBeFalse()
  })
  test('restores enrollment and recovery flows and rejects external or recursive destinations', () => {
    for (const purpose of ['Enrollment', 'Rebind', 'RecoveryEnrollment'] as const) expect(isEnrollmentFlow({ purpose })).toBeTrue()
    expect(isEnrollmentFlow({ purpose: 'Login' })).toBeFalse()
    expect(isEnrollmentFlow(null)).toBeFalse()
    expect(safeMfaReturnPath('/competitions/ctf?tag=Web')).toBe('/competitions/ctf?tag=Web')
    for (const path of ['//evil.test', '/\\evil.test', 'https://evil.test', '/auth/mfa', undefined]) expect(safeMfaReturnPath(path)).toBe('/')
  })
  test('does not refresh MFA rejection or flow requests', async () => {
    await ensureLocaleDomains(['mfa'])
    const headers = new Headers({ Authorization: 'Bearer expired' })
    expect(shouldRefreshSession(401, headers, '/api/v1/auth/mfa/verify')).toBeFalse()
    expect(shouldRefreshSession(401, headers, '/api/v1/competitions', 'LocalMfaRequired')).toBeFalse()
    expect(shouldRefreshSession(401, headers, '/api/v1/competitions')).toBeTrue()
    expect(requiresInteractiveAuthentication('PrimaryAuthenticationRequired')).toBeTrue()
    expect(requiresInteractiveAuthentication('InvalidCode')).toBeFalse()
    expect(parseApiError({ code: 'CodeAlreadyUsed' }).displayMessage).toEqual({ key: 'mfa.error.CodeAlreadyUsed', arguments: {} })
  })
})

test('MFA login restores browser-bound flow, handles invalid code and consumes successful flow once', async () => {
  const api = createMockApi({ mfa: true })
  const call = (path: string, method = 'GET', body?: unknown, cookie?: string) => api.handle(new Request(`http://noctf.mock/api/v1${path}`, { method, headers: { 'Content-Type': 'application/json', ...(cookie ? { cookie } : {}) }, body: body ? JSON.stringify(body) : undefined }))
  const login = await call('/auth/login', 'POST', { login: 'player', password: 'Mock123!' })
  const value = await login.json() as { state: string; flow: { id: string }; accessToken?: string }
  expect(value.state).toBe('MfaRequired'); expect(value.accessToken).toBeUndefined()
  const cookie = `noctf_mock_mfa=${value.flow.id}`
  expect((await call('/auth/mfa/flow')).status).toBe(410)
  expect((await call('/auth/mfa/flow', 'GET', undefined, cookie)).status).toBe(200)
  expect((await call('/auth/mfa/verify', 'POST', { code: '111111', method: 'Totp' }, cookie)).status).toBe(400)
  const verified = await call('/auth/mfa/verify', 'POST', { code: '123456', method: 'Totp' }, cookie)
  expect((await verified.json() as { state: string }).state).toBe('Authenticated')
  expect((await call('/auth/mfa/verify', 'POST', { code: '123456', method: 'Totp' }, cookie)).status).toBe(410)
})
