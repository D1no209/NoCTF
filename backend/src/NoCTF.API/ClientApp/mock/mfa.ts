import type { Data } from './schema'
import { date } from './schema'
type Context = { users: Data[]; signIn: (user: Data) => Response; userFor: (request: Request) => Data | undefined; loginRequiresMfa: boolean }
const codes = () => Array.from({ length: 10 }, (_, index) => `${index.toString(16).padStart(8, '0')}-12345678-90ABCDEF-12345678`)
export function createMockMfa(context: Context) {
  const enrolled = new Set(context.loginRequiresMfa ? context.users.filter(user => user.kind !== 'Bot').map(user => user.userId) : [])
  const required = new Set<string>()
  const remaining = new Map<string, number>()
  const flows = new Map<string, Data>()
  let policy = 'Optional'
  const json = (body: unknown, status = 200) => Response.json(body, { status, headers: { 'Cache-Control': 'no-store', 'X-NoCTF-Mock': 'true' } })
  const problem = (code: string, status = 403) => json({ code, status, title: 'Mock MFA' }, status)
  function begin(user: Data, purpose: string, account = false) {
    const flow = { id: crypto.randomUUID(), userId: user.userId, userName: user.userName, purpose, expiresAt: date(purpose === 'Login' || purpose === 'StepUp' ? 5 / 60 : 10 / 60), remainingAttempts: 5, recoveryAvailable: enrolled.has(user.userId), returnPath: '/', secret: null, provisioningUri: null, primaryAuthenticationRequired: false, account, verified: false }
    flows.set(flow.id, flow)
    return flow
  }
  function withCookie(response: Response, flow: Data) { response.headers.append('Set-Cookie', `noctf_mock_mfa=${flow.id}; Path=/; HttpOnly; SameSite=Strict`); return response }
  function login(user: Data): Response | null {
    if ((!context.loginRequiresMfa && !enrolled.has(user.userId)) || user.kind === 'Bot') return null
    const flow = begin(user, enrolled.has(user.userId) ? 'Login' : 'Enrollment')
    const response = withCookie(json({ state: flow.purpose === 'Login' ? 'MfaRequired' : 'EnrollmentRequired', flow }), flow)
    response.headers.append('Set-Cookie', 'noctf_mock_session=guest; Path=/; HttpOnly; SameSite=Strict')
    return response
  }
  async function handle(request: Request, route: string, body: Data, parameters: Data): Promise<Response | null> {
    const user = context.userFor(request)
    const id = request.headers.get('cookie')?.match(/(?:^|;\s*)noctf_mock_mfa=([^;]+)/)?.[1]
    const flow = id ? flows.get(id) : undefined
    if (route === '/auth/mfa/recovery') return problem('InvalidRecoveryGrant')
    if (route === '/auth/mfa/status') return user ? json({ enrolled: enrolled.has(user.userId), required: required.has(user.userId) || policy !== 'Optional', mandated: required.has(user.userId) || policy !== 'Optional', recoveryCodesRemaining: remaining.get(user.userId) ?? 10, recoveryMailAvailable: false, recentPrimaryAuthentication: true }) : problem('AccountUnavailable')
    if (route === '/admin/platform/mfa') return user?.role === 'Administrator' ? json({ policy, providers: [] }) : problem('NotApplicable')
    if (route === '/auth/mfa/enrollment' && user && !flow) return withCookie(json(begin(user, 'Enrollment', true)), [...flows.values()].at(-1)!)
    if (route === '/auth/mfa/step-up' && user) { const started = begin(user, 'StepUp'); started.operation = body.operation; started.targetId = body.targetId; return withCookie(json(started), started) }
    if (!route.startsWith('/auth/mfa/') && !route.includes('/mfa-')) return null
    if (!flow || flow.expiresAt <= new Date().toISOString()) return problem('FlowExpired', 410)
    const account = context.users.find(value => value.userId === flow.userId)!
    if (route === '/auth/mfa/flow' && request.method === 'DELETE') { flows.delete(flow.id); return new Response(null, { status: 204 }) }
    if (route === '/auth/mfa/flow') return json(flow)
    if (route === '/auth/mfa/enrollment') {
      flow.secret = 'JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP'
      flow.provisioningUri = `otpauth://totp/NoCTF:${encodeURIComponent(flow.userName)}?secret=${flow.secret}&issuer=NoCTF&digits=6&period=30`
      return json(flow)
    }
    if (route === '/auth/mfa/verify' || route === '/auth/mfa/step-up/verify' || route.endsWith('/enrollment/confirm')) {
      if (body.code !== '123456' && body.code !== '000000001234567890ABCDEF12345678') { flow.remainingAttempts--; return problem(flow.remainingAttempts > 0 ? 'InvalidCode' : 'AttemptsExceeded', 400) }
      if (route === '/auth/mfa/step-up/verify') { flow.verified = true; return json(flow) }
      if (flow.account !== route.includes('/account/')) return problem('NotApplicable')
      const newCodes = flow.purpose === 'Login' ? [] : codes()
      enrolled.add(flow.userId); remaining.set(flow.userId, 10); flows.delete(flow.id)
      if (flow.account) return json({ recoveryCodes: newCodes })
      const response = context.signIn(account)
      const value = await response.json() as Data; value.recoveryCodes = newCodes
      return new Response(JSON.stringify(value), { headers: response.headers, status: 200 })
    }
    if (!flow.verified || !user || user.userId !== flow.userId) return problem('StepUpRequired')
    if (route === '/auth/mfa/rebind' && flow.operation === 'RebindTotp') { flows.delete(flow.id); const next = begin(user, 'Rebind', true); return withCookie(json(next), next) }
    if (route === '/auth/mfa/disable' && flow.operation === 'DisableTotp') { enrolled.delete(user.userId); flows.delete(flow.id); return json({ userId: user.userId, tokenVersion: 1 }) }
    if (route === '/auth/mfa/recovery-codes/regenerate' && flow.operation === 'RegenerateRecoveryCodes') { flows.delete(flow.id); remaining.set(user.userId, 10); return json({ codes: codes() }) }
    if (route === '/admin/platform/mfa-policy' && flow.operation === 'ChangePolicy') { policy = body.policy; flows.delete(flow.id); return json({ policy }) }
    if (route.endsWith('/mfa-requirement') && flow.operation === 'ChangeAccountRequirement' && flow.targetId === parameters.userId) { if (body.required) required.add(parameters.userId); else required.delete(parameters.userId); flows.delete(flow.id); return json({ userId: parameters.userId, tokenVersion: 1 }) }
    return problem('NotApplicable')
  }
  return { handle, login }
}
