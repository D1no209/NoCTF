import { sourceFile } from './support/feature-source'
import { describe, expect, test } from 'bun:test'
import {
  validateAppealStatement,
  validateCompetitionQuestionDraft,
} from '../app/lib/participant-form-validation'

describe('participant action validation', () => {
  test('explains the appeal minimum instead of silently skipping the request', () => {
    expect(validateAppealStatement('测试申诉功能')).toContain('至少需要 16 个字符')
    expect(validateAppealStatement('这是一个足够详细且客观的申诉理由说明')).toBeNull()
  })

  test('requires a challenge for challenge questions and accepts valid drafts', () => {
    expect(validateCompetitionQuestionDraft({
      requiresChallenge: true,
      challengeId: null,
      title: '测试咨询',
      body: '测试咨询',
    })).toBe('题目相关咨询必须选择一个关联题目。')

    expect(validateCompetitionQuestionDraft({
      requiresChallenge: true,
      challengeId: 'challenge-1',
      title: '测试咨询',
      body: '测试咨询',
    })).toBeNull()
  })
})

describe('participant action page wiring', () => {
  test('submits appeals explicitly and keeps failures visible in the dialog', async () => {
    const page = await sourceFile(
      new URL('../app/pages/competitions/[id]/my/team.vue', import.meta.url),
    ).text()

    expect(page).toContain('type="button" class="w-full" :disabled="appealPending" @click="submitAppeal"')
    expect(page).toContain('role="alert" class="text-sm text-destructive"')
    expect(page).toContain('finally {')
    expect(page).toContain('appealPending.value = false')
  })

  test('uses a larger consultation dialog with explicit submission feedback', async () => {
    const page = await sourceFile(
      new URL('../app/pages/competitions/[id]/questions.vue', import.meta.url),
    ).text()

    expect(page).toContain('sm:max-w-2xl')
    expect(page).toContain('class="min-h-44"')
    expect(page).toContain(':disabled="createPending || createSubject === \'Challenge\' && (challengesLoading || Boolean(challengeLoadError))"')
    expect(page).toContain('@click="submitCreate"')
    expect(page).toContain("notifications.validation.relatedQuestionsRequired")
    expect(page).toContain('<Alert v-else-if="challengeLoadError" variant="destructive">')
    expect(page).toContain('@click="loadChallengeOptions"')
  })

  test('uses only the canonical password reset route', async () => {
    const page = await sourceFile(
      new URL('../app/pages/auth/password-reset.vue', import.meta.url),
    ).text()

    expect(page).toContain("definePageMeta({ middleware: 'guest' })")
    expect(page).not.toContain('alias:')
  })

  test('offers an anonymous non-disclosing verification email resend flow', async () => {
    const register = await sourceFile(
      new URL('../app/pages/auth/register.vue', import.meta.url),
    ).text()
    const verification = await sourceFile(
      new URL('../app/pages/auth/verify-email.vue', import.meta.url),
    ).text()

    expect(register).toContain('authenticationRequestEmailVerification')
    expect(register).toContain('verificationEmailQueued')
    expect(register).toContain('@click="resendVerification"')
    expect(verification).toContain('authenticationRequestEmailVerification')
    expect(verification).toContain('v-model="email"')
    expect(verification).toContain("isLoggedIn.value")
    expect(register).not.toContain("common.description.verificationEmailSentCheck")
  })

  test('keeps password visibility toggles out of the sequential form focus order', async () => {
    const component = await sourceFile(
      new URL('../app/components/ui/password-input/PasswordInput.vue', import.meta.url),
    ).text()
    const pages = await Promise.all([
      '../app/pages/auth/login.vue',
      '../app/pages/auth/register.vue',
      '../app/pages/auth/password-reset.vue',
      '../app/features/account/AccountPanel.vue',
    ].map(path => sourceFile(new URL(path, import.meta.url)).text()))

    expect(component).toContain(":type=\"visible ? 'text' : 'password'\"")
    expect(component).toContain(":aria-label=\"$t(visible ? 'common.label.hidePassword' : 'common.label.showPassword')\"")
    expect(component).toContain(':aria-pressed="visible"')
    expect(component).toContain('tabindex="-1"')
    expect(pages.every(page => page.includes('<PasswordInput'))).toBe(true)
    expect(pages.some(page => page.includes('type="password"'))).toBe(false)
  })
})
