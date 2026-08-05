import { describe, expect, test } from 'bun:test'
import en from '../src/locales/en.json'
import zhCN from '../src/locales/zh-CN.json'

const loginSource = await Bun.file(
  new URL('../src/components/auth/LoginWorkspace.vue', import.meta.url),
).text()
const forgotSource = await Bun.file(
  new URL('../src/components/auth/ForgotPasswordWorkspace.vue', import.meta.url),
).text()
const resetSource = await Bun.file(
  new URL('../src/components/auth/ResetPasswordWorkspace.vue', import.meta.url),
).text()
const routerSource = await Bun.file(new URL('../src/router/index.ts', import.meta.url)).text()

describe('password recovery workspace', () => {
  test('registers anonymous named routes and links from login without hardcoded API paths', () => {
    expect(routerSource).toContain('name: \'forgot-password\'')
    expect(routerSource).toContain('name: \'reset-password\'')
    expect(loginSource).toContain(':to="{ name: \'forgot-password\' }"')
    expect(forgotSource).toContain('authApi.requestPasswordReset')
    expect(resetSource).toContain('authApi.completePasswordReset')
    expect(forgotSource).not.toContain('/api/v1')
    expect(resetSource).not.toContain('/api/v1')
    expect(forgotSource).not.toContain('/auth/password-reset')
    expect(resetSource).not.toContain('/auth/password-reset')
  })

  test('keeps request results generic and clears local sessions after completion', () => {
    expect(forgotSource).toContain('requested.value = true')
    expect(forgotSource).toContain('t(\'auth.resetRequestPrivacyNotice\')')
    expect(resetSource).toContain('auth.logout()')
    expect(resetSource).toContain('query: { passwordReset: \'success\' }')
    expect(resetSource).toContain('router.replace({ name: \'reset-password\' })')
  })

  test.each([
    ['en', en],
    ['zh-CN', zhCN],
  ])('ships complete recovery copy for %s', (_, messages) => {
    expect(messages.auth.forgotPassword).toBeTruthy()
    expect(messages.auth.resetRequestPrivacyNotice).toBeTruthy()
    expect(messages.auth.resetLinkInvalidDescription).toBeTruthy()
    expect(messages.errors.requestPasswordReset).toBeTruthy()
    expect(messages.errors.completePasswordReset).toBeTruthy()
  })
})
