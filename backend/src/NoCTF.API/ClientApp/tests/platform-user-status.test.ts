import { describe, expect, test } from 'bun:test'

const pageUrl = new URL('../app/pages/admin/platform/users.vue', import.meta.url)

describe('platform user account status management', () => {
  test('uses the generated status endpoint and keeps the sheet open on failure', async () => {
    const source = await Bun.file(pageUrl).text()

    expect(source).toContain('adminPlatformUpdateUserAccountStatus')
    expect(source).toContain("body: { accountStatus: pendingAccountStatus.value }")
    expect(source).toContain('accountStatusConflictMessage(apiError.code) ?? apiError.message')
    const handler = source.slice(
      source.indexOf('async function saveAccountStatus'),
      source.indexOf('async function invalidateTokens'),
    )
    expect(handler).not.toContain('detailOpen.value = false')
  })

  test('prevents repeat saves, self-deactivation, and anonymized restoration', async () => {
    const source = await Bun.file(pageUrl).text()

    expect(source).toContain('accountStatusSaving.value = true')
    expect(source).toContain('accountStatusSaving.value = false')
    expect(source).toContain("detail.id === currentUser?.userId || detail.accountStatus === 'Anonymized'")
    expect(source).toContain("detail.value.accountStatus === 'Anonymized'")
    expect(source).toContain("pendingAccountStatus === detail.accountStatus")
  })

  test('shows explicit last-administrator and anonymized-account failures', async () => {
    const source = await Bun.file(pageUrl).text()

    expect(source).toContain("case 'LastAdministratorProtected':")
    expect(source).toContain("translate('不能停用最后一名有效管理员')")
    expect(source).toContain("case 'AnonymizedAccountImmutable':")
    expect(source).toContain("translate('已匿名化账户不可恢复')")
  })
})
