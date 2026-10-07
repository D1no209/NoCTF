import type { NoCtfApplicationAuthenticationMfaMfaVerificationMethod, NoCtfapiEndpointsAuthenticationMfaMfaFlowResponse } from '../../../api'
export function normalizeMfaCode(code: string, method: NoCtfApplicationAuthenticationMfaMfaVerificationMethod): string {
  return method === 'Totp' ? code.replace(/\s/g, '') : code.trim().replaceAll('-', '').toUpperCase()
}
export function validMfaCode(code: string, method: NoCtfApplicationAuthenticationMfaMfaVerificationMethod): boolean {
  return method === 'Totp' ? /^\d{6}$/.test(normalizeMfaCode(code, method)) : /^[0-9A-F]{32}$/.test(normalizeMfaCode(code, method))
}
export function isEnrollmentFlow(flow: NoCtfapiEndpointsAuthenticationMfaMfaFlowResponse | null): boolean {
  return flow?.purpose === 'Enrollment' || flow?.purpose === 'Rebind' || flow?.purpose === 'RecoveryEnrollment'
}
export function safeMfaReturnPath(value: unknown): string {
  return typeof value === 'string' && value.startsWith('/') && !value.startsWith('//') && !value.includes('\\')
    && !value.startsWith('/auth/') ? value : '/'
}
