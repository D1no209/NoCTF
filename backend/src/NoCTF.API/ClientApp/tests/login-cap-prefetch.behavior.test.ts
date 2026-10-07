import { describe, expect, test } from 'bun:test'
import { computed, effectScope, nextTick, onScopeDispose, ref, watch } from 'vue'
import { createHumanVerificationCoordinator } from '../app/lib/human-verification-coordinator'
import { bindViewState } from '../app/features/shared/view-state'

const compile = async (path: string) => new Bun.Transpiler({ loader: 'ts' })
  .transformSync(await Bun.file(new URL(path, import.meta.url)).text())
  .replace(/^import[\s\S]*?from ["'][^"']+["'];?\s*$/gm, '')
  .replace(/export function /g, 'function ')
  .replace(/await import\(["']@cap\.js\/widget["']\)/g, 'await loadCap()')
const verificationSource = await compile('../app/features/security/useHumanVerification.ts')
const loginSource = await compile('../app/features/routes/auth/useAuthLoginPage.ts')
const drain = async () => { await nextTick(); await new Promise(resolve => setTimeout(resolve, 0)) }

function harness(provider = 'Cap', delayConfiguration = false) {
  const scopes = [effectScope()]
  const mounted: Array<() => void> = []
  const challenges: FakeCap[] = []
  const logins: Array<{ name: string; password: string; headers: Record<string, string> }> = []
  const navigations: string[] = []
  const notices: unknown[] = []
  const configuration = ref({ humanVerification: { provider, siteKey: 'public-key', apiEndpoint: 'https://cap.test/site/' } })
  let configurationLoaded: () => void = () => {}
  const configurationPromise = delayConfiguration ? new Promise<void>(resolve => { configurationLoaded = resolve }) : Promise.resolve()
  let loginError: unknown = null
  let mfaRequired = false
  let sequence = 0
  const sharedState = ref(null)

  class FakeCap {
    token: string | null = null
    removed = false
    listeners = new Map<string, Array<(event: any) => void>>()
    resolve!: (value: { success: boolean; token: string }) => void
    reject!: (reason: unknown) => void
    result = new Promise<{ success: boolean; token: string }>((resolve, reject) => { this.resolve = resolve; this.reject = reject })
    widget = { remove: () => { this.removed = true; this.expire() } }
    constructor() { challenges.push(this) }
    addEventListener(type: string, listener: (event: any) => void) {
      this.listeners.set(type, [...(this.listeners.get(type) ?? []), listener])
    }
    solve() { return this.result }
    emit(type: string, detail: object = {}) { for (const listener of this.listeners.get(type) ?? []) listener({ detail }) }
    complete() {
      this.token = `proof-${++sequence}`
      this.emit('progress', { progress: 100 })
      this.resolve({ success: true, token: this.token })
    }
    expire() { this.token = null; this.emit('reset') }
    fail() { this.reject(new Error('offline')) }
  }

  const dependencies = {
    computed, ref, watch, onScopeDispose, createHumanVerificationCoordinator, bindViewState,
    usePasskeyLogin: () => ({
      available: ref(false), pending: ref(false), error: ref(null),
      signIn: async () => {}, cancel: () => {},
    }),
    onMounted: (callback: () => void) => mounted.push(callback),
    useState: () => sharedState,
    usePlatform: () => ({ configuration, ensureLoaded: () => configurationPromise }),
    useRoute: () => ({ query: { redirect: '/admin/competitions/example' }, fullPath: '/auth/login' }),
    useAuth: () => ({ login: async (name: string, password: string, headers: Record<string, string>) => {
      logins.push({ name, password, headers }); if (loginError) throw loginError
      if (mfaRequired) { navigations.push('/auth/mfa'); scopes[0]!.stop(); return false }
      return true
    } }),
    useAuthThemeArtwork: () => ({ authArtwork: ref(null) }),
    loadCap: async () => ({ default: FakeCap }),
    window: {}, navigator: { hardwareConcurrency: 8 },
    describeMessage: (key: string) => ({ key }),
    translate: (key: string) => key,
    parseApiError: (_: unknown, fallback: any) => ({ displayMessage: fallback }),
    toast: { success: (message: unknown) => notices.push(message), error: (message: unknown) => notices.push(message), info: (message: unknown) => notices.push(message) },
    authenticationSsoListProviders: async () => ({ data: { items: [] } }),
    authenticationSsoBeginLogin: async () => ({}),
    navigateTo: async (path: string) => { navigations.push(path); scopes[0]!.stop() },
  }
  const factory = new Function('dependencies', `const { ${Object.keys(dependencies).join(', ')} } = dependencies;
    ${verificationSource}; ${loginSource}; return { useAuthLoginPage, useHumanVerification };`)(dependencies)
  const state = scopes[0]!.run(() => factory.useAuthLoginPage())!
  const mount = async () => { for (const callback of mounted) callback(); await drain() }
  const credentials = () => { state.loginName.value = 'alice'; state.password.value = 'secret' }
  return {
    state, mount, credentials, challenges, logins, navigations, notices, configurationLoaded,
    rejectLogin: () => { loginError = new Error('invalid credentials') },
    requireMfa: () => { mfaRequired = true },
    stop: () => { for (const scope of scopes) scope.stop() },
    stopPage: () => scopes[0]!.stop(),
    extraVerification: () => {
      const scope = effectScope(); scopes.push(scope)
      return { scope, verification: scope.run(() => factory.useHumanVerification())! }
    },
  }
}

describe('login CAP precomputation', () => {
  test('starts on entry without credentials, then consumes the prepared proof once', async () => {
    const app = harness()
    try {
      await app.mount()
      expect(app.challenges).toHaveLength(1)
      expect(app.state.capVerification.value.state).toBe('running')
      expect(app.state.pending.value).toBe(false)
      expect(app.logins).toHaveLength(0)
      app.challenges[0]!.complete(); await drain()
      expect(app.state.capVerification.value.state).toBe('success')
      expect(app.challenges[0]!.removed).toBe(false)
      app.credentials(); await app.state.submit(); await drain()
      expect(app.logins).toEqual([{ name: 'alice', password: 'secret', headers: { 'X-NoCTF-Human-Verification': 'proof-1' } }])
      expect(app.navigations).toEqual(['/admin/competitions/example'])
      expect(app.challenges[0]!.removed).toBe(true)
      expect(app.challenges).toHaveLength(1)
    }
    finally { app.stop() }
  })

  test('submission waits for the existing computation and ignores duplicate clicks', async () => {
    const app = harness()
    try {
      await app.mount(); app.credentials()
      const submitted = app.state.submit(); await drain()
      await app.state.submit()
      expect(app.state.pending.value).toBe(true)
      expect(app.logins).toHaveLength(0)
      expect(app.challenges).toHaveLength(1)
      app.challenges[0]!.complete(); await submitted
      expect(app.logins).toHaveLength(1)
    }
    finally { app.stop() }
  })

  test('preserves the MFA continuation without announcing authentication or redirecting past it', async () => {
    const app = harness()
    try {
      await app.mount(); app.challenges[0]!.complete(); await drain()
      app.credentials(); app.requireMfa(); await app.state.submit(); await drain()
      expect(app.logins).toHaveLength(1)
      expect(app.navigations).toEqual(['/auth/mfa'])
      expect(app.notices).toHaveLength(0)
      expect(app.state.password.value).toBe('')
      expect(app.challenges).toHaveLength(1)
    }
    finally { app.stop() }
  })

  test('renews an expired proof automatically before another login attempt', async () => {
    const app = harness()
    try {
      await app.mount(); app.challenges[0]!.complete(); await drain()
      app.challenges[0]!.expire(); await drain()
      expect(app.challenges).toHaveLength(2)
      expect(app.challenges[0]!.removed).toBe(true)
      expect(app.state.capVerification.value.state).toBe('running')
      app.credentials(); const submitted = app.state.submit(); await drain()
      app.challenges[1]!.complete(); await submitted
      expect(app.logins[0]!.headers['X-NoCTF-Human-Verification']).toBe('proof-2')
    }
    finally { app.stop() }
  })

  test('retries computation without submitting an empty form', async () => {
    const app = harness()
    try {
      await app.mount(); app.challenges[0]!.fail(); await drain()
      expect(app.state.capCanRetry.value).toBe(true)
      app.state.retryInlineCap(); await drain()
      app.challenges[1]!.complete(); await drain()
      expect(app.state.capVerification.value.state).toBe('success')
      expect(app.logins).toHaveLength(0)
      expect(app.state.error.value).toBe(null)
    }
    finally { app.stop() }
  })

  test('renews a proof that expires just as the user submits', async () => {
    const app = harness()
    try {
      await app.mount(); app.challenges[0]!.complete(); await drain()
      app.challenges[0]!.expire(); app.credentials()
      const submitted = app.state.submit(); await drain()
      expect(app.challenges).toHaveLength(2)
      expect(app.logins).toHaveLength(0)
      app.challenges[1]!.complete(); await submitted
      expect(app.logins[0]!.headers['X-NoCTF-Human-Verification']).toBe('proof-2')
    }
    finally { app.stop() }
  })

  test('a failed in-flight submission continues after retrying the same verification grant', async () => {
    const app = harness()
    try {
      await app.mount(); app.credentials()
      const submitted = app.state.submit(); await drain()
      app.challenges[0]!.fail(); await drain()
      expect(app.state.capCanRetry.value).toBe(true)
      expect(app.state.pending.value).toBe(true)
      app.state.retryInlineCap(); await drain()
      app.challenges[1]!.complete(); await submitted
      expect(app.logins).toHaveLength(1)
      expect(app.challenges).toHaveLength(2)
    }
    finally { app.stop() }
  })

  test('prepares a fresh proof after rejected credentials instead of reusing the consumed one', async () => {
    const app = harness()
    try {
      await app.mount(); app.challenges[0]!.complete(); await drain()
      app.credentials(); app.rejectLogin(); await app.state.submit(); await drain()
      expect(app.state.error.value.key).toBe('auth.login.invalidCredentials')
      expect(app.challenges).toHaveLength(2)
      const submitted = app.state.submit(); await drain()
      app.challenges[1]!.complete(); await submitted; await drain()
      expect(app.logins.map(login => login.headers['X-NoCTF-Human-Verification'])).toEqual(['proof-1', 'proof-2'])
    }
    finally { app.stop() }
  })

  test('cancels pending verification when leaving and never logs in from a late response', async () => {
    const app = harness()
    await app.mount(); app.credentials()
    const submitted = app.state.submit(); await drain()
    app.stop(); await submitted
    app.challenges[0]!.complete(); await drain()
    expect(app.challenges[0]!.removed).toBe(true)
    expect(app.logins).toHaveLength(0)
    expect(app.challenges).toHaveLength(1)
  })

  test('does not start computation if configuration arrives after the page was disposed', async () => {
    const app = harness('Cap', true)
    await app.mount(); app.stop(); app.configurationLoaded(); await drain()
    expect(app.challenges).toHaveLength(0)
  })

  test('keeps disabled and dialog providers idle on page entry', async () => {
    for (const provider of ['None', 'Turnstile']) {
      const app = harness(provider)
      try {
        await app.mount()
        expect(app.challenges).toHaveLength(0)
        expect(app.state.capVerification.value).toBe(null)
        expect(app.logins).toHaveLength(0)
        if (provider === 'None') {
          app.credentials(); await app.state.submit()
          expect(app.logins[0]!.headers).toEqual({})
        }
      }
      finally { app.stop() }
    }
  })

  test('an old page scope cannot cancel another requester verification', async () => {
    const app = harness()
    try {
      await app.mount(); app.challenges[0]!.complete(); await drain()
      const next = app.extraVerification()
      const verified = next.verification.request('login', 'login-inline'); await drain()
      app.stopPage()
      expect(app.challenges[1]!.removed).toBe(false)
      app.challenges[1]!.complete()
      expect(await verified).toEqual({ 'X-NoCTF-Human-Verification': 'proof-2' })
    }
    finally { app.stop() }
  })
})
