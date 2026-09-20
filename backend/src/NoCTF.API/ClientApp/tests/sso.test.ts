import { describe, expect, test } from 'bun:test'
import { sourceFile } from './support/feature-source'

describe('single sign-on client integration', () => {
  test('uses generated endpoints and keeps provider tokens out of browser state', async () => {
    const login = await sourceFile(new URL('../app/pages/auth/login.vue', import.meta.url)).text()
    const completion = await sourceFile(new URL('../app/pages/auth/sso/complete.vue', import.meta.url)).text()
    const auth = await Bun.file(new URL('../app/composables/useAuth.ts', import.meta.url)).text()

    expect(login).toContain('authenticationSsoListProviders')
    expect(login).toContain('authenticationSsoBeginLogin')
    expect(completion).toContain('authenticationSsoGetFlow')
    expect(completion).toContain('authenticationSsoCompleteBinding')
    expect(auth).toContain('authenticationSsoCompleteLogin')
    expect(completion).not.toContain('access_token')
    expect(completion).not.toContain('id_token')
    expect(completion).not.toContain('ticket')
  })

  test('places binding under account security and provider management in its own workspace route', async () => {
    const account = await sourceFile(new URL('../app/features/account/AccountPanel.vue', import.meta.url)).text()
    const administration = await sourceFile(new URL('../app/pages/admin/platform/authentication.vue', import.meta.url)).text()
    const navigation = await Bun.file(new URL('../app/features/routes/admin/useAdminPlatformPage.ts', import.meta.url)).text()

    expect(account).toContain('authenticationSsoGetMyBinding')
    expect(account).toContain('authenticationSsoBeginBinding')
    expect(account).toContain('authenticationSsoUnbindIdentity')
    expect(administration).toContain('adminPlatformSsoGetConfiguration')
    expect(administration).toContain('adminPlatformSsoReplaceProviderSecret')
    expect(navigation).toContain("'/admin/platform/authentication'")
  })
})
