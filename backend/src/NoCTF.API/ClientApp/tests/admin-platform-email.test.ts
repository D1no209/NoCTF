import { expect, test } from 'bun:test'
import { sourceFile } from './support/feature-source'

test('email administration exposes persisted human-verification providers and secrets', async () => {
  const controller = await sourceFile('app/features/routes/admin/platform/useAdminPlatformEmailPage.ts').text()
  const view = await sourceFile('app/components/views/page/admin/platform/AdminPlatformEmailPageView.vue').text()
  const types = await sourceFile('app/api/types.gen.ts').text()

  expect(controller).toContain('data.humanVerification')
  expect(controller).toContain('body: { humanVerification: humanVerificationRequest() }')
  expect(controller).toContain('adminPlatformReplaceHumanVerificationSecret')
  expect(controller).toContain('await refreshPlatform()')
  expect(view).toContain('v-model="humanForm.provider"')
  expect(view).toContain('v-model="humanForm.enabled"')
  expect(view).toContain('v-model="humanForm.runtimeEnabled"')
  expect(controller).toContain('runtimeEnabled: humanForm.runtimeEnabled')
  expect(view).toContain('v-model="humanForm.evaluationEnabled"')
  expect(controller).toContain('evaluationEnabled: humanForm.evaluationEnabled')
  expect(view).toContain("humanForm.provider === 'Cap'")
  expect(view).toContain("humanForm.provider === 'Turnstile'")
  expect(view).toContain("$t('ui.configureProviderSecret')")
  expect(types).toContain('PlatformHumanVerificationPatchRequest')
  expect(types).toContain('ReplaceHumanVerificationSecretRequest')
})

test('email administration keeps a retryable page body when configuration loading fails', async () => {
  const controller = await sourceFile('app/features/routes/admin/platform/useAdminPlatformEmailPage.ts').text()
  const view = await sourceFile('app/components/views/page/admin/platform/AdminPlatformEmailPageView.vue').text()

  expect(controller).toContain('loadError.value = parseApiError(error).message')
  expect(controller).toContain('load,')
  expect(view).toContain('v-else-if="!configuration"')
  expect(view).toContain('@click="load"')
  expect(view).toContain("$t('ui.retry')")
})

test('SMTP test uses only an enabled and fully saved configuration', async () => {
  const controller = await sourceFile('app/features/routes/admin/platform/useAdminPlatformEmailPage.ts').text()
  const view = await sourceFile('app/components/views/page/admin/platform/AdminPlatformEmailPageView.vue').text()

  expect(controller).toContain('const emailDirty = computed(')
  expect(controller).toContain('syncForm(data.emailVerification)')
  expect(view).toContain('!configuration.enabled || emailDirty')
  expect(view).not.toContain('sendingTest || !form.enabled')
})
