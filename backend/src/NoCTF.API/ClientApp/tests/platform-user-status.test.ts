import { sourceFile } from './support/feature-source'
import { describe, expect, test } from 'bun:test'

const pageUrl = new URL('../app/pages/admin/platform/users.vue', import.meta.url)

describe('platform user account status management', () => {
  test('merges Bot filtering and creation into the user management page', async () => {
    const source = await sourceFile(pageUrl).text()
    const platform = await sourceFile(
      new URL('../app/pages/admin/platform.vue', import.meta.url),
    ).text()
    expect(source).toContain("kind: roleFilter.value === 'Bot' ? 'Bot'")
    expect(source).toContain('keyword: search.value.trim() || null')
    expect(source).toContain('<SelectItem value="Bot">')
    expect(source).toContain('adminPlatformCreateBot({')
    expect(source).toContain('roleFilter.value = \'Bot\'')
    expect(source).toContain("$t('ui.createBot')")
    expect(source).toContain('validation="feature"')
    expect(platform).not.toContain("to: '/admin/platform/bots'")
  })

  test('leaves the initial skeleton after the first paged response', async () => {
    const source = await sourceFile(pageUrl).text()

    expect(source).toContain('const loading = computed(() => pagination.loading.value && !pagination.initialized.value)')
    expect(source).not.toContain('const loading = ref(true)')
    expect(source).toContain('<div v-if="loading"')
    expect(source).toContain(':loading="pageLoading"')
  })

  test('uses the generated status endpoint and keeps the sheet open on failure', async () => {
    const source = await sourceFile(pageUrl).text()

    expect(source).toContain('adminPlatformPatchUser')
    expect(source).toContain("body: { accountStatus: pendingAccountStatus.value }")
    expect(source).toContain('accountStatusConflictMessage(apiError.code) ?? apiError.message')
    const handler = source.slice(
      source.indexOf('async function saveAccountStatus'),
      source.indexOf('async function invalidateTokens'),
    )
    expect(handler).not.toContain('detailOpen.value = false')
  })

  test('prevents repeat saves, self-deactivation, and anonymized restoration', async () => {
    const source = await sourceFile(pageUrl).text()

    expect(source).toContain('accountStatusSaving.value = true')
    expect(source).toContain('accountStatusSaving.value = false')
    expect(source).toContain("detail.id === currentUser?.userId || detail.accountStatus === 'Anonymized'")
    expect(source).toContain("detail.value.accountStatus === 'Anonymized'")
    expect(source).toContain("pendingAccountStatus === detail.accountStatus")
  })

  test('shows explicit last-administrator and anonymized-account failures', async () => {
    const source = await sourceFile(pageUrl).text()

    expect(source).toContain("case 'LastAdministratorProtected':")
    expect(source).toContain("translate(\"ui.theLastActiveAdministratorCannotBeDeactivated\")")
    expect(source).toContain("case 'AnonymizedAccountImmutable':")
    expect(source).toContain("translate(\"ui.anAnonymizedAccountCannotBeRestored\")")
  })

  test('manages email activation independently through the generated SDK', async () => {
    const source = await sourceFile(pageUrl).text()

    expect(source).toContain('adminPlatformPatchUser')
    expect(source).toContain("body: { emailVerified: pendingEmailVerification.value === 'Verified' }")
    expect(source).toContain("(pendingEmailVerification === 'Verified') === detail.emailVerified")
    expect(source).toContain("$t('ui.emailActivationStatus')")
    expect(source).toContain("$t('ui.activated')")
    expect(source).toContain("$t('ui.notActivated')")
    const handler = source.slice(
      source.indexOf('async function saveEmailVerification'),
      source.indexOf('async function invalidateTokens'),
    )
    expect(handler).not.toContain('detailOpen.value = false')
  })

  test('lists searches filters and removes administrator-visible SSO bindings', async () => {
    const source = await sourceFile(pageUrl).text()

    expect(source).toContain('ssoProviderId: ssoProviderFilter.value')
    expect(source).toContain('adminPlatformSsoGetConfiguration')
    expect(source).toContain('adminPlatformUnbindSsoIdentity')
    expect(source).toContain('user.ssoBinding.subject')
    expect(source).toContain('detail.ssoBinding.boundAt')
    expect(source).toContain("translate('sso.adminUnbindSuccessful')")
    expect(source).toContain("await navigateTo('/auth/login')")
  })
})
