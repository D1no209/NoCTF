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
    expect(account).toContain('body: { providerId: ssoProviderId.value }')
    expect(account).toContain('authenticationSsoUnbindIdentity()')
    expect(account).not.toContain('ssoPassword')
    expect(account).not.toContain('account-panel-sso-bind-password')
    expect(account).not.toContain('account-panel-sso-unbind-password')
    expect(administration).toContain('adminPlatformSsoGetConfiguration')
    expect(administration).toContain('adminPlatformSsoReplaceProviderSecret')
    expect(navigation).toContain("'/admin/platform/authentication'")
  })

  test('renders configured provider icons without leaking the current page as referrer', async () => {
    const login = await sourceFile(new URL('../app/components/views/page/auth/AuthLoginPageView.vue', import.meta.url)).text()
    const account = await sourceFile(new URL('../app/components/views/account/AccountPanelView.vue', import.meta.url)).text()
    const administration = await sourceFile(new URL('../app/components/views/page/admin/platform/AdminPlatformAuthenticationPageView.vue', import.meta.url)).text()
    const controller = await sourceFile(new URL('../app/features/routes/admin/platform/useAdminPlatformAuthenticationPage.ts', import.meta.url)).text()

    expect(login).toContain('provider.iconUrl')
    expect(account).toContain('providerIconUrl')
    expect(administration).toContain('sso-provider-icon-url')
    expect(controller).toContain('iconUrl: providerForm.iconUrl.trim() || null')
    expect(login).toContain('referrerpolicy="no-referrer"')
    expect(account).toContain('referrerpolicy="no-referrer"')
  })

  test('discards an unlinked login flow and guides local authentication into a fresh binding', async () => {
    const completion = await sourceFile(new URL('../app/pages/auth/sso/complete.vue', import.meta.url)).text()
    const registration = await sourceFile(new URL('../app/pages/auth/register.vue', import.meta.url)).text()
    const account = await sourceFile(new URL('../app/features/account/AccountPanel.vue', import.meta.url)).text()

    expect(completion).toContain("parsed.code === 'IdentityNotLinked'")
    expect(completion).toContain("new URLSearchParams({ account: 'security' })")
    expect(completion).toContain("query.set('ssoProvider', providerId)")
    expect(completion).toContain('sso.signInToBind')
    expect(completion).toContain('sso.registerToBind')
    expect(registration).toContain('loginTarget')
    expect(account).toContain("account !== 'security'")
    expect(account).toContain('await loadSsoBinding()')
    expect(account).toContain('ssoProviderId.value = requestedProvider')
    expect(completion).not.toContain('LinkRequired')
  })
})
